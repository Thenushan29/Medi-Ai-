using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.AiPipeline.RuleChecks;

/// <summary>
/// Stage 3 of the pipeline (§11.1): findings that are matters of arithmetic and set membership,
/// not of clinical judgement — duplicates (FR-5.1), dosage conflicts (FR-5.2), allergy and printed-
/// warning contradictions (FR-5.4, FR-5.5), and out-of-range lab values (FR-6.3).
///
/// These run in code because they are exactly reproducible and cannot hallucinate. The LLM stage
/// that follows handles what genuinely needs clinical reasoning: drug–drug interactions.
///
/// Everything here is <c>DetectedBy = "rules"</c>, so the interface can honestly distinguish a
/// computed finding from an AI-proposed one.
/// </summary>
public interface IRuleChecker
{
    Task<IReadOnlyList<Alert>> CheckAsync(Guid patientId, CancellationToken ct = default);
}

public sealed class DeterministicRuleChecker(
    MediTrailDbContext db,
    ILogger<DeterministicRuleChecker> logger) : IRuleChecker
{
    public async Task<IReadOnlyList<Alert>> CheckAsync(Guid patientId, CancellationToken ct = default)
    {
        var medications = await db.Medications
            .AsNoTracking()
            .Where(m => m.PatientId == patientId && m.GenericName != null)
            .ToListAsync(ct);

        var allergies = await db.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .ToListAsync(ct);

        var labs = await db.LabResults
            .AsNoTracking()
            .Where(l => l.PatientId == patientId && l.IsOutOfRange)
            .ToListAsync(ct);

        // Two uploads of the same file are one visit filed twice, not two prescribing events.
        // The evaluation dataset contains exactly this (traps.md Y2), and reporting it as
        // double-dosing would be a false alarm about a filing artefact.
        var hashByDocument = await db.Documents
            .AsNoTracking()
            .Where(d => d.PatientId == patientId)
            .ToDictionaryAsync(d => d.Id, d => d.Sha256, ct);

        var alerts = new List<Alert>();

        alerts.AddRange(FindDuplicates(patientId, medications, hashByDocument));
        alerts.AddRange(FindDosageConflicts(patientId, medications, hashByDocument));
        alerts.AddRange(FindDuplicateTherapeuticClass(patientId, medications));
        alerts.AddRange(FindAllergyAndWarningConflicts(patientId, medications, allergies));
        alerts.AddRange(FindOutOfRangeLabs(patientId, labs));

        logger.LogInformation("Rule checks raised {Count} alert(s) for patient {PatientId}",
            alerts.Count, patientId);

        return alerts;
    }

    /// <summary>
    /// The same generic prescribed in overlapping periods (FR-5.1).
    ///
    /// Two rows from the *same document* are not a duplicate — a prescription listing a drug twice
    /// is one prescribing decision. Nor are two rows from byte-identical uploads: the dataset
    /// contains the same file twice (traps.md Y2), and reporting that as double-dosing would be a
    /// false alarm about a filing artefact.
    /// </summary>
    private static IEnumerable<Alert> FindDuplicates(
        Guid patientId, List<Medication> medications, Dictionary<Guid, string> hashByDocument)
    {
        foreach (var group in medications.GroupBy(m => m.GenericName!))
        {
            var items = group.ToList();
            if (items.Count < 2) continue;

            for (var i = 0; i < items.Count; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    var (a, b) = (items[i], items[j]);

                    if (IsSameVisit(a, b, hashByDocument)) continue;
                    if (!PeriodsOverlap(a, b)) continue;

                    yield return new Alert
                    {
                        PatientId = patientId,
                        Type = AlertType.DuplicatePrescription,
                        Severity = AlertSeverity.Amber,
                        Title = $"{Display(group.Key)} prescribed twice over the same period",
                        InvolvedGenerics = [group.Key],
                        ExplanationEn =
                            $"{Display(group.Key)} appears on two different documents with overlapping " +
                            $"dates{Prescribers(a, b)}. Taking both at once would mean a double dose.",
                        SuggestedActionEn =
                            "Show both prescriptions to your pharmacist and ask which one to follow.",
                        Confidence = CombinedConfidence(a.Confidence, b.Confidence),
                        RequiresProfessionalConsult = true,
                        VerificationStatus = VerificationStatus.NotApplicable,
                        EvidenceDocumentIds = [a.DocumentId, b.DocumentId],
                        DetectedBy = "rules"
                    };
                }
            }
        }
    }

    /// <summary>
    /// The same generic at conflicting strength or daily frequency (FR-5.2).
    /// Compared numerically, never by asking a model whether two strings differ.
    /// </summary>
    private static IEnumerable<Alert> FindDosageConflicts(
        Guid patientId, List<Medication> medications, Dictionary<Guid, string> hashByDocument)
    {
        foreach (var group in medications.GroupBy(m => m.GenericName!))
        {
            var items = group.Where(m => m.StrengthValue is not null || m.FrequencyPerDay is not null).ToList();
            if (items.Count < 2) continue;

            for (var i = 0; i < items.Count; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    var (a, b) = (items[i], items[j]);
                    if (IsSameVisit(a, b, hashByDocument)) continue;

                    var strengthDiffers = a.StrengthValue is not null && b.StrengthValue is not null
                        && a.StrengthValue != b.StrengthValue
                        && string.Equals(a.StrengthUnit, b.StrengthUnit, StringComparison.OrdinalIgnoreCase);

                    var frequencyDiffers = a.FrequencyPerDay is not null && b.FrequencyPerDay is not null
                        && a.FrequencyPerDay != b.FrequencyPerDay;

                    if (!strengthDiffers && !frequencyDiffers) continue;

                    yield return new Alert
                    {
                        PatientId = patientId,
                        Type = AlertType.DosageConflict,
                        Severity = AlertSeverity.Amber,
                        Title = $"{Display(group.Key)} prescribed at different doses",
                        InvolvedGenerics = [group.Key],
                        ExplanationEn =
                            $"{Display(group.Key)} is written as {Dose(a)} on one document and " +
                            $"{Dose(b)} on another. These do not match.",
                        SuggestedActionEn =
                            "Ask your doctor or pharmacist which dose is the current one.",
                        Confidence = CombinedConfidence(a.Confidence, b.Confidence),
                        RequiresProfessionalConsult = true,
                        VerificationStatus = VerificationStatus.NotApplicable,
                        EvidenceDocumentIds = [a.DocumentId, b.DocumentId],
                        DetectedBy = "rules"
                    };
                }
            }
        }
    }

    /// <summary>
    /// Two different drugs from one therapeutic class taken together — the dataset carries three
    /// beta-blockers (traps.md Y3). The generics differ, so the duplicate check above cannot see it.
    /// </summary>
    private static IEnumerable<Alert> FindDuplicateTherapeuticClass(Guid patientId, List<Medication> medications)
    {
        var classified = medications
            .Select(m => (Medication: m, Class: DrugNameNormalizer.ClassOf(m.GenericName)))
            .Where(x => x.Class is not null)
            .ToList();

        foreach (var group in classified.GroupBy(x => x.Class!))
        {
            var distinctDrugs = group
                .GroupBy(x => x.Medication.GenericName!)
                .ToList();

            if (distinctDrugs.Count < 2) continue;

            var names = distinctDrugs.Select(g => Display(g.Key)).ToList();
            var documents = group.Select(x => x.Medication.DocumentId).Distinct().ToList();
            var confidences = group.Select(x => x.Medication.Confidence).ToArray();

            yield return new Alert
            {
                PatientId = patientId,
                Type = AlertType.DuplicatePrescription,
                Severity = AlertSeverity.Red,
                Title = $"{names.Count} {group.Key}s in your records",
                InvolvedGenerics = distinctDrugs.Select(g => g.Key).ToList(),
                ExplanationEn =
                    $"{string.Join(", ", names)} all belong to the same group of medicines " +
                    $"({group.Key}s). They do the same job, so taking more than one together can " +
                    "have a much stronger effect than intended.",
                SuggestedActionEn =
                    "Bring all of these to your doctor or pharmacist and ask whether you should be taking more than one.",
                Confidence = CombinedConfidence(confidences),
                RequiresProfessionalConsult = true,
                VerificationStatus = VerificationStatus.NotApplicable,
                EvidenceDocumentIds = documents,
                DetectedBy = "rules"
            };
        }
    }

    /// <summary>
    /// A medication matching a recorded allergy (FR-5.4) or a warning printed on any document,
    /// including the same one (FR-5.5).
    ///
    /// **This is the check the headline trap turns on.** It works only because both sides went
    /// through <see cref="DrugNameNormalizer"/>, which maps acetaminophen to paracetamol.
    /// </summary>
    private static IEnumerable<Alert> FindAllergyAndWarningConflicts(
        Guid patientId, List<Medication> medications, List<Allergy> allergies)
    {
        foreach (var entry in allergies)
        {
            foreach (var substance in entry.RelatesTo)
            {
                var matches = medications
                    .Where(m => DrugNameNormalizer.AreSameDrug(m.GenericName, substance)
                             || IsClassMatch(m.GenericName, substance))
                    .ToList();

                if (matches.Count == 0) continue;

                var sameDocument = matches.Any(m => m.DocumentId == entry.DocumentId);
                var drugName = Display(matches[0].GenericName!);

                var documents = matches.Select(m => m.DocumentId).Append(entry.DocumentId).Distinct().ToList();

                yield return new Alert
                {
                    PatientId = patientId,
                    Type = entry.IsDocumentWarning ? AlertType.DocumentWarningConflict : AlertType.AllergyConflict,
                    Severity = AlertSeverity.Red,
                    Title = entry.IsDocumentWarning
                        ? sameDocument
                            ? $"{drugName} was prescribed despite a warning on the same document"
                            : $"{drugName} conflicts with a warning in your records"
                        : $"{drugName} conflicts with a recorded allergy",
                    InvolvedGenerics = matches.Select(m => m.GenericName!).Distinct().ToList(),
                    ExplanationEn = entry.IsDocumentWarning
                        ? BuildWarningExplanation(drugName, substance, entry, sameDocument)
                        : $"{drugName} was prescribed, and your records list an allergy to " +
                          $"{Display(substance)}" +
                          (entry.Reaction is null ? "." : $" ({entry.Reaction}).")
                    ,
                    SuggestedActionEn =
                        "Do not change anything yourself. Show this to your doctor or pharmacist before your next dose.",
                    Confidence = CombinedConfidence(matches.Select(m => m.Confidence).Append(entry.Confidence).ToArray()),
                    RequiresProfessionalConsult = true,
                    // Established by the patient's own documents — one says avoid it, another
                    // prescribes it. There is nothing for an external drug database to add, and
                    // leaving this Pending would show "verification pending" forever.
                    VerificationStatus = VerificationStatus.NotApplicable,
                    EvidenceDocumentIds = documents,
                    DetectedBy = "rules"
                };
            }
        }
    }

    private static string BuildWarningExplanation(
        string drugName, string substance, Allergy entry, bool sameDocument)
    {
        var warning = entry.Substance ?? entry.SourceText ?? "a printed warning";

        var opening = sameDocument
            ? $"This document prescribes {drugName}, while its own advice section says: \"{warning}\"."
            : $"{drugName} was prescribed, but another document in your records says: \"{warning}\".";

        // The point of the finding is usually that the two names are the same molecule — say so,
        // because a reader who does not know that will not see the contradiction.
        var equivalence = DrugNameNormalizer.AreSameDrug(drugName, substance)
                          && !string.Equals(drugName, substance, StringComparison.OrdinalIgnoreCase)
            ? $" {Display(substance)} and {drugName} are the same medicine under different names."
            : string.Empty;

        return opening + equivalence;
    }

    private static IEnumerable<Alert> FindOutOfRangeLabs(Guid patientId, List<LabResult> labs)
    {
        foreach (var lab in labs.Where(l => l.TestNameStandard is not null))
        {
            var range = lab.NormalRangeText
                ?? $"{lab.NormalMin?.ToString() ?? "?"}–{lab.NormalMax?.ToString() ?? "?"}";

            var direction = lab.NormalMax is not null && lab.ValueNumeric > lab.NormalMax ? "above" : "below";

            yield return new Alert
            {
                PatientId = patientId,
                Type = AlertType.LabOutOfRange,
                Severity = AlertSeverity.Amber,
                Title = $"{lab.TestName ?? lab.TestNameStandard} is outside the normal range",
                InvolvedGenerics = [],
                ExplanationEn =
                    $"Your {lab.TestName ?? lab.TestNameStandard} was {lab.ValueNumeric}{Unit(lab)}, " +
                    $"which is {direction} the normal range printed on the report ({range}).",
                SuggestedActionEn = "Ask your doctor what this result means for you.",
                Confidence = lab.Confidence ?? 70,
                RequiresProfessionalConsult = false,
                VerificationStatus = VerificationStatus.NotApplicable,
                EvidenceDocumentIds = [lab.DocumentId],
                DetectedBy = "rules"
            };
        }
    }

    // ---- helpers ----

    /// <summary>
    /// A penicillin allergy has to catch amoxicillin, so a class name on either side counts as a
    /// match. Only for allergy and warning text — not for duplicate detection.
    /// </summary>
    private static bool IsClassMatch(string? genericName, string substance)
    {
        var drugClass = DrugNameNormalizer.ClassOf(genericName);
        return drugClass is not null
            && string.Equals(drugClass, DrugNameNormalizer.Normalize(substance), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when two rows describe the same prescribing event: the same document, or two documents
    /// that are byte-identical uploads of it. The second case matters because people upload a
    /// folder and the same scan lands twice — a real occurrence in the evaluation dataset.
    /// </summary>
    private static bool IsSameVisit(Medication a, Medication b, Dictionary<Guid, string> hashByDocument)
    {
        if (a.DocumentId == b.DocumentId) return true;

        return hashByDocument.TryGetValue(a.DocumentId, out var hashA)
            && hashByDocument.TryGetValue(b.DocumentId, out var hashB)
            && hashA == hashB;
    }

    /// <summary>
    /// Two prescriptions overlap when their windows intersect. An unknown end date is treated as
    /// non-overlapping rather than open-ended — guessing "still taking it" would manufacture
    /// duplicates out of every repeated medication in a long history.
    /// </summary>
    private static bool PeriodsOverlap(Medication a, Medication b)
    {
        if (a.StartDate is null || b.StartDate is null) return false;

        var aEnd = a.EndDate ?? a.StartDate.Value;
        var bEnd = b.EndDate ?? b.StartDate.Value;

        return a.StartDate <= bEnd && b.StartDate <= aEnd;
    }

    private static string Display(string generic) =>
        generic.Length == 0 ? generic : char.ToUpperInvariant(generic[0]) + generic[1..];

    private static string Dose(Medication m)
    {
        var strength = m.StrengthValue is not null ? $"{m.StrengthValue}{m.StrengthUnit}" : "an unstated strength";
        var frequency = m.FrequencyPerDay is not null ? $" {m.FrequencyPerDay}× a day" : string.Empty;
        return strength + frequency;
    }

    private static string Unit(LabResult lab) =>
        string.IsNullOrWhiteSpace(lab.Unit) ? string.Empty : " " + lab.Unit;

    private static string Prescribers(Medication a, Medication b) =>
        a.DocumentId == b.DocumentId ? string.Empty : " from different visits";

    /// <summary>
    /// The lowest input confidence, not the average: a finding is only as trustworthy as the
    /// weakest reading it rests on (§11.4).
    /// </summary>
    private static int CombinedConfidence(params int?[] confidences)
    {
        var known = confidences.Where(c => c is not null).Select(c => c!.Value).ToList();
        return known.Count == 0 ? 60 : known.Min();
    }
}
