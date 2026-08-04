using MediTrail.Api.Contracts.Extraction;

namespace MediTrail.GoldenRunner;

public enum Outcome
{
    /// <summary>Expected and actual agree.</summary>
    Correct,
    /// <summary>Both say "could not read". Counts as correct — a null is a real answer (FR-3.6).</summary>
    CorrectNull,
    /// <summary>Actual differs from expected.</summary>
    Wrong,
    /// <summary>Expected a value, model returned null. Cautious: a miss, not a hallucination.</summary>
    Missed,
    /// <summary>Expected null, model produced a value. This is the dangerous one (§3.3: target 0).</summary>
    Hallucinated
}

public sealed record FieldResult(string Category, string Field, string? Expected, string? Actual, Outcome Outcome);

/// <summary>
/// Compares one extraction against its hand-labelled ground truth, field by field (§18.1).
///
/// Two judgements are deliberate:
///   • null == null is scored Correct. The prompt's central instruction is "return null rather than
///     guess", so agreeing that something is unreadable is a success, not a gap.
///   • Producing a value where the label says null is scored separately as a hallucination, because
///     §3.3 sets a target of zero for it and averaging it into "wrong" would hide it.
/// </summary>
public static class FieldComparer
{
    public static List<FieldResult> Compare(DocumentExtraction expected, DocumentExtraction actual)
    {
        var results = new List<FieldResult>();

        Add(results, "document", "documentType", expected.DocumentType, actual.DocumentType);
        Add(results, "dates", "documentDate", expected.DocumentDate, actual.DocumentDate);
        Add(results, "document", "provider", expected.Provider?.Name, actual.Provider?.Name);

        CompareMedications(results, expected.Medications, actual.Medications);
        CompareLabResults(results, expected.LabResults, actual.LabResults);
        CompareAllergies(results, expected.Allergies, actual.Allergies);
        CompareWarnings(results, expected.WarningsInDocument, actual.WarningsInDocument);

        return results;
    }

    // Medications are matched on generic name where present, brand otherwise — position in the list
    // is not meaningful and must not be treated as identity.
    private static void CompareMedications(
        List<FieldResult> results,
        IReadOnlyList<ExtractedMedication> expected,
        IReadOnlyList<ExtractedMedication> actual)
    {
        var remaining = actual.ToList();

        // Every printed word in the document, for deciding whether an unmatched extraction is
        // genuinely invented or merely named differently from the label.
        var documentText = string.Join(" ", expected.Select(m =>
            $"{m.SourceText} {m.BrandName} {m.GenericName}"));

        foreach (var want in expected)
        {
            var key = want.GenericName ?? want.BrandName;

            var got = remaining.FirstOrDefault(m => Matches(m.GenericName, key) || Matches(m.BrandName, key))
                // "belladonna" vs "belladonna tincture" is the same drug named differently.
                // Without this, one naming difference is penalised twice — once as a miss and
                // again as a hallucination — which corrupts the metric that is supposed to mean
                // "invented a drug that is not on the page".
                ?? remaining.FirstOrDefault(m => NearlyMatches(m.GenericName, key)
                                              || NearlyMatches(m.BrandName, key)
                                              || NearlyMatches(m.GenericName, want.BrandName));

            if (got is null)
            {
                results.Add(new FieldResult("medications", $"{key} (whole entry)", key, null, Outcome.Missed));
                continue;
            }

            remaining.Remove(got);

            var source = want.SourceText;

            Add(results, "medications", $"{key}.genericName", want.GenericName, got.GenericName, source);
            Add(results, "medications", $"{key}.brandName", want.BrandName, got.BrandName, source);
            Add(results, "strengths", $"{key}.strength", Format(want.StrengthValue), Format(got.StrengthValue), source);
            Add(results, "strengths", $"{key}.strengthUnit", want.StrengthUnit, got.StrengthUnit, source);
            Add(results, "frequencies", $"{key}.frequencyPerDay", Format(want.FrequencyPerDay), Format(got.FrequencyPerDay), source);
            Add(results, "frequencies", $"{key}.durationDays", want.DurationDays?.ToString(), got.DurationDays?.ToString(), source);
        }

        // Anything left over matched no labelled medication. It is only a hallucination if the
        // name appears nowhere in the document either — otherwise it is a splitting or naming
        // difference, which is wrong but not invented.
        foreach (var extra in remaining)
        {
            var name = extra.GenericName ?? extra.BrandName ?? "(unnamed)";
            var grounded = IsGrounded(name, documentText) || IsGrounded(name, extra.SourceText);

            results.Add(new FieldResult(
                "medications",
                grounded ? $"{name} (extra entry)" : $"{name} (not in document)",
                null, name,
                grounded ? Outcome.Wrong : Outcome.Hallucinated));
        }
    }

    private static void CompareLabResults(
        List<FieldResult> results,
        IReadOnlyList<ExtractedLabResult> expected,
        IReadOnlyList<ExtractedLabResult> actual)
    {
        var remaining = actual.ToList();

        foreach (var want in expected)
        {
            var key = want.TestNameStandard ?? want.TestName;
            var got = remaining.FirstOrDefault(l =>
                Matches(l.TestNameStandard, key) || Matches(l.TestName, key) || Matches(l.TestName, want.TestName));

            if (got is null)
            {
                results.Add(new FieldResult("labValues", $"{key} (whole entry)", key, null, Outcome.Missed));
                continue;
            }

            remaining.Remove(got);

            var source = want.SourceText;

            Add(results, "labValues", $"{key}.value", Format(want.ValueNumeric) ?? want.ValueText,
                Format(got.ValueNumeric) ?? got.ValueText, source);
            Add(results, "labValues", $"{key}.unit", want.Unit, got.Unit, source);
            Add(results, "labValues", $"{key}.normalMin", Format(want.NormalMin), Format(got.NormalMin), source);
            Add(results, "labValues", $"{key}.normalMax", Format(want.NormalMax), Format(got.NormalMax), source);
            Add(results, "dates", $"{key}.testDate", want.TestDate, got.TestDate, source);
        }

        foreach (var extra in remaining)
        {
            var name = extra.TestNameStandard ?? extra.TestName ?? "(unnamed)";
            results.Add(new FieldResult("labValues", $"{name} (not in document)", null, name, Outcome.Hallucinated));
        }
    }

    private static void CompareAllergies(
        List<FieldResult> results,
        IReadOnlyList<ExtractedAllergy> expected,
        IReadOnlyList<ExtractedAllergy> actual)
    {
        var remaining = actual.ToList();

        foreach (var want in expected)
        {
            var key = want.SubstanceGeneric ?? want.Substance;
            var got = remaining.FirstOrDefault(a => Matches(a.SubstanceGeneric, key) || Matches(a.Substance, key));

            if (got is null)
            {
                results.Add(new FieldResult("allergies", $"{key} (whole entry)", key, null, Outcome.Missed));
                continue;
            }

            remaining.Remove(got);
            Add(results, "allergies", $"{key}.substanceGeneric", want.SubstanceGeneric, got.SubstanceGeneric);
        }

        foreach (var extra in remaining)
        {
            var name = extra.SubstanceGeneric ?? extra.Substance ?? "(unnamed)";
            results.Add(new FieldResult("allergies", $"{name} (not in document)", null, name, Outcome.Hallucinated));
        }
    }

    /// <summary>
    /// Warnings are scored on <c>relatesTo</c>, not on wording — the exact sentence does not matter,
    /// but the generics it names do, because that is what FR-5.5 matches medications against.
    /// </summary>
    private static void CompareWarnings(
        List<FieldResult> results,
        IReadOnlyList<ExtractedWarning> expected,
        IReadOnlyList<ExtractedWarning> actual)
    {
        var actualGenerics = actual.SelectMany(w => w.RelatesTo).Select(Canonical).ToHashSet();

        foreach (var want in expected)
        {
            foreach (var generic in want.RelatesTo)
            {
                var found = actualGenerics.Contains(Canonical(generic));
                results.Add(new FieldResult("warnings", $"warning refers to {generic}", generic,
                    found ? generic : null, found ? Outcome.Correct : Outcome.Missed));
            }
        }

        if (expected.Count > 0 || actual.Count > 0)
        {
            results.Add(new FieldResult("warnings", "warning count",
                expected.Count.ToString(), actual.Count.ToString(),
                expected.Count == actual.Count ? Outcome.Correct : Outcome.Wrong));
        }
    }

    /// <summary>
    /// <paramref name="grounding"/> is the label's own <c>sourceText</c> for this item.
    ///
    /// A hallucination is a value that **is not in the document** — not merely a field the labeller
    /// chose to leave empty. When a label records only <c>genericName: "amoxicillin"</c> and the
    /// model also fills <c>brandName: "Amoxicillin"</c>, the word is printed right there on the
    /// page; scoring that as invention would punish the model for the labeller's shorthand and
    /// bury real hallucinations in noise.
    /// </summary>
    private static void Add(List<FieldResult> results, string category, string field,
        string? expected, string? actual, string? grounding = null)
    {
        var outcome = (Blank(expected), Blank(actual)) switch
        {
            (true, true) => Outcome.CorrectNull,
            (true, false) => IsGrounded(actual!, grounding) ? Outcome.Correct : Outcome.Hallucinated,
            (false, true) => Outcome.Missed,
            _ => Matches(expected, actual) ? Outcome.Correct : Outcome.Wrong
        };

        results.Add(new FieldResult(category, field, expected, actual, outcome));
    }

    /// <summary>True when the value actually appears in the document text the label recorded.</summary>
    private static bool IsGrounded(string actual, string? grounding)
    {
        if (Blank(grounding)) return false;

        var haystack = Canonical(grounding!);
        var needle = Canonical(actual);

        return needle.Length > 0 && haystack.Contains(needle, StringComparison.Ordinal);
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool Matches(string? a, string? b) =>
        !Blank(a) && !Blank(b) && Canonical(a!) == Canonical(b!);

    /// <summary>
    /// One name contains the other — "belladonna" / "belladonna tincture", "aspirin" /
    /// "aspirin and codeine". Same drug, different level of detail. Used only to pair entries so
    /// their fields can be compared; the fields themselves are still scored strictly.
    /// </summary>
    private static bool NearlyMatches(string? a, string? b)
    {
        if (Blank(a) || Blank(b)) return false;

        var left = Canonical(a!);
        var right = Canonical(b!);

        if (left == right) return true;

        // Require a substantial shared prefix, so "aspirin" does not pair with "asparaginase".
        var shorter = left.Length <= right.Length ? left : right;
        var longer = left.Length <= right.Length ? right : left;

        return shorter.Length >= 5 && longer.Contains(shorter, StringComparison.Ordinal);
    }

    /// <summary>Case, spacing and punctuation are not the model's job to get right.</summary>
    private static string Canonical(string value) =>
        new(value.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '.').ToArray());

    private static string? Format(decimal? value) =>
        value?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
