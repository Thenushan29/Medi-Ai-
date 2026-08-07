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
                        ExplanationTa = RuleFindingTamil.DuplicatePrescription(Display(group.Key)),
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
                        ExplanationTa = RuleFindingTamil.DosageConflict(Display(group.Key), a, b),
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
    ///
    /// Scoped in time like the interaction check (§11.1): the finding is "you may be taking two of
    /// these at once", which is only true of prescriptions whose windows meet. A beta-blocker
    /// stopped in 2007 and another started in 2019 is a change of therapy, not double therapy.
    /// </summary>
    private static IEnumerable<Alert> FindDuplicateTherapeuticClass(Guid patientId, List<Medication> medications)
    {
        var classified = medications
            .Select(m => (Medication: m, Class: DrugNameNormalizer.ClassOf(m.GenericName)))
            .Where(x => x.Class is not null)
            .ToList();

        foreach (var group in classified.GroupBy(x => x.Class!))
        {
            var byGeneric = group
                .GroupBy(x => x.Medication.GenericName!)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Medication).ToList());

            if (byGeneric.Count < 2) continue;

            foreach (var cluster in ConcurrentClusters(byGeneric))
            {
                var names = cluster.Generics.Select(Display).ToList();
                var rows = cluster.Generics.SelectMany(g => byGeneric[g]).ToList();

                var caveat = cluster.DatesUnknown
                    ? " " + MedicationWindowCalculator.DateUnknownCaveatEn
                    : string.Empty;

                var tamil = RuleFindingTamil.DuplicateTherapeuticClass(names, group.Key);

                yield return new Alert
                {
                    PatientId = patientId,
                    Type = AlertType.DuplicatePrescription,
                    Severity = AlertSeverity.Red,
                    Title = $"{names.Count} {group.Key}s in your records",
                    InvolvedGenerics = cluster.Generics,
                    ExplanationEn =
                        $"{string.Join(", ", names)} all belong to the same group of medicines " +
                        $"({group.Key}s). They do the same job, so taking more than one together can " +
                        "have a much stronger effect than intended." + caveat,
                    ExplanationTa = tamil is null || !cluster.DatesUnknown
                        ? tamil
                        : tamil + " " + MedicationWindowCalculator.DateUnknownCaveatTa,
                    SuggestedActionEn =
                        "Bring all of these to your doctor or pharmacist and ask whether you should be taking more than one.",
                    Confidence = CombinedConfidence(rows.Select(m => m.Confidence).ToArray()),
                    RequiresProfessionalConsult = true,
                    VerificationStatus = VerificationStatus.NotApplicable,
                    EvidenceDocumentIds = rows.Select(m => m.DocumentId).Distinct().ToList(),
                    DetectedBy = "rules"
                };
            }
        }
    }

    /// <summary>
    /// Groups the generics of one therapeutic class into sets the patient could plausibly have been
    /// taking together, by walking the "concurrent with" relation.
    ///
    /// Membership is transitive on purpose: if A overlapped B and B overlapped C, the patient was
    /// on two of this class at some point either way, which is exactly what the finding claims.
    /// A pair whose concurrency could not be established (an unreadable document date) still links —
    /// dropping it would let a failed extraction silently delete a real finding — but the cluster
    /// then carries the caveat.
    /// </summary>
    private static IEnumerable<ClassCluster> ConcurrentClusters(Dictionary<string, List<Medication>> byGeneric)
    {
        var generics = byGeneric.Keys.ToList();
        var seen = new HashSet<string>();

        foreach (var root in generics)
        {
            if (!seen.Add(root)) continue;

            var cluster = new List<string> { root };
            var queue = new Queue<string>([root]);
            var datesUnknown = false;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var candidate in generics)
                {
                    if (seen.Contains(candidate)) continue;

                    var verdict = MedicationWindowCalculator.Compare(byGeneric[current], byGeneric[candidate]);
                    if (verdict == Concurrency.NotConcurrent) continue;

                    datesUnknown |= verdict == Concurrency.DateUnknown;

                    seen.Add(candidate);
                    cluster.Add(candidate);
                    queue.Enqueue(candidate);
                }
            }

            if (cluster.Count < 2) continue;

            yield return new ClassCluster(cluster, datesUnknown);
        }
    }

    /// <summary>Generics of one class that were, or may have been, in use at the same time.</summary>
    private sealed record ClassCluster(List<string> Generics, bool DatesUnknown);

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
                    ExplanationTa = entry.IsDocumentWarning
                        ? RuleFindingTamil.DocumentWarningConflict(
                            drugName, Display(substance), entry.SourceText ?? entry.Substance,
                            sameDocument, IsSameMedicineUnderAnotherName(drugName, substance))
                        : RuleFindingTamil.AllergyConflict(drugName, Display(substance), entry.Reaction),
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
        // Quote the printed sentence, not the substance column: "avoid liver-toxic medications
        // (e.g. acetaminophen)" is the contradiction the reader has to see, and Substance now
        // holds only the drug name.
        var warning = entry.SourceText ?? entry.Substance ?? "a printed warning";

        var opening = sameDocument
            ? $"This document prescribes {drugName}, while its own advice section says: \"{warning}\"."
            : $"{drugName} was prescribed, but another document in your records says: \"{warning}\".";

        // The point of the finding is usually that the two names are the same molecule — say so,
        // because a reader who does not know that will not see the contradiction.
        var equivalence = IsSameMedicineUnderAnotherName(drugName, substance)
            ? $" {Display(substance)} and {drugName} are the same medicine under different names."
            : string.Empty;

        return opening + equivalence;
    }

    private static bool IsSameMedicineUnderAnotherName(string drugName, string substance) =>
        DrugNameNormalizer.AreSameDrug(drugName, substance)
        && !string.Equals(drugName, substance, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Alert> FindOutOfRangeLabs(Guid patientId, List<LabResult> labs)
    {
        foreach (var lab in labs.Where(l => l.TestNameStandard is not null))
        {
            var range = lab.NormalRangeText
                ?? $"{lab.NormalMin?.ToString() ?? "?"}–{lab.NormalMax?.ToString() ?? "?"}";

            var above = lab.NormalMax is not null && lab.ValueNumeric > lab.NormalMax;
            var direction = above ? "above" : "below";
            var testName = lab.TestName ?? lab.TestNameStandard!;

            yield return new Alert
            {
                PatientId = patientId,
                Type = AlertType.LabOutOfRange,
                Severity = AlertSeverity.Amber,
                Title = $"{testName} is outside the normal range",
                InvolvedGenerics = [],
                ExplanationEn =
                    $"Your {testName} was {lab.ValueNumeric}{Unit(lab)}, " +
                    $"which is {direction} the normal range printed on the report ({range}).",
                ExplanationTa = RuleFindingTamil.LabOutOfRange(
                    testName, lab.ValueNumeric, Unit(lab), range, above),
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
