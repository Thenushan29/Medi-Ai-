using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.Data.Entities;

namespace MediTrail.GoldenRunner.Traps;

internal enum TrapOutcome { Pass, Fail, Skipped }

internal sealed record TrapResult(string Id, string Description, TrapOutcome Outcome, string Detail);

/// <summary>
/// The assertions from <c>dataset/golden/traps.md</c>, evaluated against what the pipeline actually
/// wrote.
///
/// Nothing here reaches into the detection logic to help a trap along. A check that cannot find its
/// finding reports the observed value and fails; diagnosing why is a separate decision, and a
/// harness that quietly adjusted the rules to go green would be worse than no harness.
/// </summary>
internal static class TrapChecks
{
    /// <summary>Which patient set each trap needs, so a filtered run knows what it cannot judge.</summary>
    public static readonly IReadOnlyDictionary<string, string> PatientOf = new Dictionary<string, string>
    {
        ["Y1"] = "y",
        ["Y2"] = "y",
        ["Y3"] = "y",
        ["X1"] = "x",
        ["Y10"] = "y",
        ["Y11"] = "y",
        ["X6"] = "x"
    };

    public static IReadOnlyList<string> All => [.. PatientOf.Keys];

    public static TrapResult Evaluate(string id, PatientRun? run) => id switch
    {
        "Y1" => Y1(run),
        "Y2" => Y2(run),
        "Y3" => Y3(run),
        "X1" => X1(run),
        "Y10" => NullDate(run, "Y10", "patient_y_year3_5", "the placeholder date \"Jan 9, 20yy\""),
        "Y11" => NullDate(run, "Y11", "patient_y_year3_6", "the ambiguous date \"09-11-12\""),
        "X6" => X6(run),
        _ => new TrapResult(id, "unknown trap", TrapOutcome.Skipped, "No such trap id.")
    };

    // -----------------------------------------------------------------------
    // Y1 — the headline finding (FR-5.5)
    // -----------------------------------------------------------------------

    private const string Y1Description =
        "Same-document contradiction: paracetamol prescribed on patient_y_year2_1 while that page's " +
        "own advice warns against acetaminophen";

    private static TrapResult Y1(PatientRun? run)
    {
        if (run is null) return Skipped("Y1", Y1Description);

        var alert = run.Alerts.FirstOrDefault(a =>
            a.Type == AlertType.DocumentWarningConflict
            && a.InvolvedGenerics.Any(g => DrugNameNormalizer.AreSameDrug(g, "paracetamol")));

        // The dataset has no allergy rows at all, so this finding can only have come through the
        // printed-warning path. Proving that is part of the assertion, not a footnote.
        var warnings = run.Allergies
            .Where(a => a.IsDocumentWarning
                     && a.RelatesTo.Any(r => DrugNameNormalizer.AreSameDrug(r, "paracetamol")))
            .ToList();

        var recordedAllergies = run.Allergies.Count(a => !a.IsDocumentWarning);

        if (alert is null)
        {
            var reason = warnings.Count == 0
                ? "No document-warning row relates to paracetamol/acetaminophen — the warning never " +
                  "survived extraction or normalization, so the rule check had nothing to match."
                : $"The warning row exists ({Describe(warnings[0])}) but no DocumentWarningConflict " +
                  "alert names paracetamol — the break is in the rule check, not the extraction.";

            var paracetamol = run.Medications
                .Where(m => DrugNameNormalizer.AreSameDrug(m.GenericName, "paracetamol"))
                .Select(m => $"{m.Document}:{m.GenericName}")
                .ToList();

            return new TrapResult("Y1", Y1Description, TrapOutcome.Fail,
                $"{reason} Paracetamol medication rows: " +
                $"{(paracetamol.Count == 0 ? "none" : string.Join(", ", paracetamol))}. " +
                $"Warning rows relating to paracetamol: {warnings.Count}. " +
                $"Recorded allergies (non-warning rows): {recordedAllergies}.");
        }

        var path = recordedAllergies == 0
            ? "warningsInDocument path (AlertType.DocumentWarningConflict); the patient set has zero " +
              "recorded-allergy rows, so the allergy path cannot have contributed"
            : $"warningsInDocument path (AlertType.DocumentWarningConflict); note {recordedAllergies} " +
              "recorded-allergy row(s) also exist in this set, which traps.md does not expect";

        return new TrapResult("Y1", Y1Description, TrapOutcome.Pass,
            $"\"{alert.Title}\" — {alert.Severity}, confidence {alert.Confidence}, " +
            $"consult={alert.RequiresProfessionalConsult}, evidence {Join(alert.EvidenceDocuments)}. " +
            $"Raised through the {path}. " +
            $"Backing warning: {(warnings.Count == 0 ? "none found" : Describe(warnings[0]))}");
    }

    // -----------------------------------------------------------------------
    // Y2 — a byte-identical re-upload is one visit filed twice (FR-2.6)
    // -----------------------------------------------------------------------

    private const string Y2Description =
        "patient_y_year3_3 is byte-identical to patient_y_year3_2: served from the extraction cache, " +
        "and not reported as a duplicate prescription";

    private static TrapResult Y2(PatientRun? run)
    {
        if (run is null) return Skipped("Y2", Y2Description);

        var first = run.Document("patient_y_year3_2");
        var second = run.Document("patient_y_year3_3");

        if (first is null || second is null)
        {
            return new TrapResult("Y2", Y2Description, TrapOutcome.Fail,
                "One of the two documents is missing from the run.");
        }

        var problems = new List<string>();

        var hashesMatch = first.Sha256 == second.Sha256;
        if (!hashesMatch)
        {
            problems.Add($"file hashes differ ({first.Sha256[..12]} vs {second.Sha256[..12]})");
        }

        // Exactly one of the pair should have been sent to the model. Which one wins depends on
        // queue order, so the assertion is on the pair, not on a particular file.
        var cached = new[] { first, second }.Count(d => d.Status == DocumentStatus.Cached);
        if (cached != 1)
        {
            problems.Add(
                $"expected exactly one of the pair to be Cached, found {cached} " +
                $"({first.Name}={first.Status}, {second.Name}={second.Status})");
        }

        // FindDuplicates and FindDosageConflicts are the same-generic checks; the therapeutic-class
        // finding is a different rule and names two or more generics, so it is not caught here.
        var sharedGenerics = run.Medications
            .Where(m => m.Document == first.Name || m.Document == second.Name)
            .Select(m => m.GenericName)
            .Where(g => g is not null)
            .Select(g => g!.ToLowerInvariant())
            .ToHashSet();

        var falseDuplicates = run.Alerts
            .Where(a => (a.Type == AlertType.DuplicatePrescription && a.InvolvedGenerics.Count == 1)
                     || a.Type == AlertType.DosageConflict)
            .Where(a => a.InvolvedGenerics.Any(g => sharedGenerics.Contains(g.ToLowerInvariant())))
            .ToList();

        if (falseDuplicates.Count > 0)
        {
            problems.Add("duplicate/dosage alert(s) raised over the copied file: " +
                         string.Join("; ", falseDuplicates.Select(a => $"\"{a.Title}\"")));
        }

        var howVerified =
            $"Verified end to end through the real DocumentService + ProcessingWorker cache path " +
            $"(SHA-256 {first.Sha256[..12]}…, {first.Name}={first.Status}, {second.Name}={second.Status}); " +
            $"no separate hash-only check was needed.";

        return problems.Count == 0
            ? new TrapResult("Y2", Y2Description, TrapOutcome.Pass,
                $"{howVerified} No same-generic duplicate or dosage alert over " +
                $"{sharedGenerics.Count} shared generic(s).")
            : new TrapResult("Y2", Y2Description, TrapOutcome.Fail,
                string.Join("; ", problems) + ".");
    }

    // -----------------------------------------------------------------------
    // Y3 — three beta-blockers across separate visits
    // -----------------------------------------------------------------------

    private const string Y3Description =
        "Duplicate therapeutic class covering atenolol, metoprolol and oxprenolol as beta blockers";

    private static readonly string[] BetaBlockers = ["atenolol", "metoprolol", "oxprenolol"];

    private static TrapResult Y3(PatientRun? run)
    {
        if (run is null) return Skipped("Y3", Y3Description);

        var alert = run.Alerts.FirstOrDefault(a =>
            a.Type == AlertType.DuplicatePrescription
            && BetaBlockers.All(b => a.InvolvedGenerics.Any(g => DrugNameNormalizer.AreSameDrug(g, b))));

        if (alert is not null)
        {
            return new TrapResult("Y3", Y3Description, TrapOutcome.Pass,
                $"\"{alert.Title}\" — {alert.Severity}, confidence {alert.Confidence}, " +
                $"consult={alert.RequiresProfessionalConsult}, generics {Join(alert.InvolvedGenerics)}, " +
                $"evidence {Join(alert.EvidenceDocuments)}.");
        }

        var extracted = BetaBlockers
            .Select(b => $"{b}: " + Join(run.Medications
                .Where(m => DrugNameNormalizer.AreSameDrug(m.GenericName, b))
                .Select(m => m.Document)
                .Distinct()
                .ToList()))
            .ToList();

        var partial = run.Alerts
            .Where(a => a.Type == AlertType.DuplicatePrescription && a.InvolvedGenerics.Count > 1)
            .Select(a => $"\"{a.Title}\" {Join(a.InvolvedGenerics)}")
            .ToList();

        return new TrapResult("Y3", Y3Description, TrapOutcome.Fail,
            $"No class alert names all three. Extracted as — {string.Join("; ", extracted)}. " +
            $"Class alerts present: {(partial.Count == 0 ? "none" : string.Join(", ", partial))}.");
    }

    // -----------------------------------------------------------------------
    // X1 — warfarin with aspirin
    // -----------------------------------------------------------------------

    private const string X1Description = "Drug-interaction alert for warfarin combined with aspirin";

    private static TrapResult X1(PatientRun? run)
    {
        if (run is null) return Skipped("X1", X1Description);

        var alert = run.Alerts.FirstOrDefault(a =>
            a.Type == AlertType.DrugInteraction
            && a.InvolvedGenerics.Any(g => g.Contains("warfarin", StringComparison.OrdinalIgnoreCase))
            && a.InvolvedGenerics.Any(g => g.Contains("aspirin", StringComparison.OrdinalIgnoreCase)));

        if (alert is not null)
        {
            return new TrapResult("X1", X1Description, TrapOutcome.Pass,
                $"\"{alert.Title}\" — {alert.Severity}, confidence {alert.Confidence}, " +
                $"consult={alert.RequiresProfessionalConsult}, openFDA {alert.VerificationStatus}, " +
                $"evidence {Join(alert.EvidenceDocuments)}.");
        }

        var relevant = run.Medications
            .Where(m => (m.GenericName ?? m.BrandName ?? string.Empty)
                .Contains("warfarin", StringComparison.OrdinalIgnoreCase)
                || (m.GenericName ?? m.BrandName ?? string.Empty)
                .Contains("aspirin", StringComparison.OrdinalIgnoreCase))
            .Select(m => $"{m.Document}: brand={m.BrandName ?? "null"} generic={m.GenericName ?? "null"}")
            .ToList();

        var interactions = run.Alerts
            .Where(a => a.Type == AlertType.DrugInteraction)
            .Select(a => $"\"{a.Title}\"")
            .ToList();

        return new TrapResult("X1", X1Description, TrapOutcome.Fail,
            $"Extracted rows — {(relevant.Count == 0 ? "neither drug reached the record" : string.Join("; ", relevant))}. " +
            $"Interaction alerts raised: {(interactions.Count == 0 ? "none" : string.Join(", ", interactions))}.");
    }

    // -----------------------------------------------------------------------
    // Y10 / Y11 — an unreadable date is null, never invented
    // -----------------------------------------------------------------------

    private static TrapResult NullDate(PatientRun? run, string id, string document, string what)
    {
        var description = $"{document} carries {what}: documentDate must be null, not invented";

        if (run is null) return Skipped(id, description);

        var row = run.Document(document);

        if (row is null)
        {
            return new TrapResult(id, description, TrapOutcome.Fail,
                $"{document} is missing from the run.");
        }

        var raw = row.RawDocumentDate is null or "" ? "null" : $"\"{row.RawDocumentDate}\"";

        return row.DocumentDate is null
            ? new TrapResult(id, description, TrapOutcome.Pass,
                $"documentDate is null. Model returned {raw} before normalization.")
            : new TrapResult(id, description, TrapOutcome.Fail,
                $"INVENTED DATE: documentDate is {row.DocumentDate:yyyy-MM-dd}. " +
                $"Model returned {raw} before normalization.");
    }

    // -----------------------------------------------------------------------
    // X6 — a placeholder is not a drug
    // -----------------------------------------------------------------------

    private const string X6Description =
        "DEMO MEDICINE 1..4 placeholders are not resolved to real generic names";

    private static TrapResult X6(PatientRun? run)
    {
        if (run is null) return Skipped("X6", X6Description);

        var placeholders = run.Medications
            .Where(m => IsPlaceholder(m.BrandName) || IsPlaceholder(m.GenericName)
                     || (m.SourceText?.Contains("demo medicine", StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        // Two failure shapes: a placeholder handed a real generic, and a placeholder written into
        // the generic column as if it were a drug name.
        var invented = placeholders.Where(m => m.GenericName is not null).ToList();

        if (invented.Count > 0)
        {
            return new TrapResult("X6", X6Description, TrapOutcome.Fail,
                "HALLUCINATION: " + string.Join("; ", invented.Select(m =>
                    $"{m.Document}: brand={m.BrandName ?? "null"} -> generic=\"{m.GenericName}\"")));
        }

        var documents = placeholders.Select(m => m.Document).Distinct().OrderBy(d => d).ToList();

        return new TrapResult("X6", X6Description, TrapOutcome.Pass,
            $"{placeholders.Count} placeholder row(s) across {Join(documents)}, every one with " +
            "genericName null. None entered a cross-check.");
    }

    private static bool IsPlaceholder(string? name) => DrugNameNormalizer.IsPlaceholder(name);

    // -----------------------------------------------------------------------

    private static TrapResult Skipped(string id, string description) =>
        new(id, description, TrapOutcome.Skipped,
            $"Patient set '{PatientOf[id]}' was not part of this run.");

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(none)" : string.Join(", ", values);

    private static string Describe(AllergyRow warning) =>
        $"{warning.Document} relatesTo=[{string.Join(", ", warning.RelatesTo)}] " +
        $"\"{Shorten(warning.SourceText ?? warning.Substance)}\"";

    private static string Shorten(string? text) =>
        text is null ? string.Empty
        : text.Length <= 110 ? text.ReplaceLineEndings(" ")
        : text.ReplaceLineEndings(" ")[..110] + "…";
}
