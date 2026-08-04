using System.Text.RegularExpressions;

namespace MediTrail.Api.AiPipeline.Normalization;

/// <summary>
/// Groups the same test across labs and years onto one series (FR-4.3, FR-6.1). Without this,
/// "SGPT", "ALT (SGPT)" and "Alanine transaminase" chart as three separate one-point series and
/// no trend is visible.
/// </summary>
public static partial class LabTestNormalizer
{
    /// <summary>Aliases → canonical key. Deliberately conservative; an unknown test keeps its own name.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sgpt"] = "alt", ["alt sgpt"] = "alt", ["alanine transaminase"] = "alt",
        ["alanine aminotransferase"] = "alt", ["s.g.p.t"] = "alt",

        ["sgot"] = "ast", ["ast sgot"] = "ast", ["aspartate transaminase"] = "ast",
        ["aspartate aminotransferase"] = "ast", ["s.g.o.t"] = "ast",

        ["total bilirubin"] = "bilirubin total", ["s bilirubin total"] = "bilirubin total",
        ["serum bilirubin"] = "bilirubin total", ["t bilirubin"] = "bilirubin total",
        ["direct bilirubin"] = "bilirubin direct", ["conjugated bilirubin"] = "bilirubin direct",

        ["serum creatinine"] = "creatinine", ["s creatinine"] = "creatinine",
        ["creat"] = "creatinine", ["cratine"] = "creatinine",

        ["blood urea nitrogen"] = "bun", ["urea nitrogen"] = "bun",
        ["blood urea"] = "urea", ["serum urea"] = "urea",

        ["haemoglobin"] = "hemoglobin", ["hb"] = "hemoglobin", ["hgb"] = "hemoglobin",
        ["fasting blood sugar"] = "fasting glucose", ["fbs"] = "fasting glucose",
        ["fasting blood glucose"] = "fasting glucose",
        ["random blood sugar"] = "random glucose", ["rbs"] = "random glucose",
        ["hba1c"] = "hba1c", ["glycated hemoglobin"] = "hba1c", ["glycosylated hemoglobin"] = "hba1c",

        ["total cholesterol"] = "cholesterol total", ["serum cholesterol"] = "cholesterol total",
        ["ldl cholesterol"] = "ldl", ["ldl c"] = "ldl",
        ["hdl cholesterol"] = "hdl", ["hdl c"] = "hdl",
        ["triglycerides"] = "triglycerides", ["tg"] = "triglycerides",

        ["total leukocyte count"] = "wbc", ["tlc"] = "wbc", ["white blood cell count"] = "wbc",
        ["white cell count"] = "wbc", ["leukocyte count"] = "wbc",
        ["platelet count"] = "platelets", ["plt"] = "platelets",

        ["thyroid stimulating hormone"] = "tsh",
        ["alkaline phosphatase"] = "alp", ["alk phos"] = "alp",
        ["c reactive protein"] = "crp",
        ["erythrocyte sedimentation rate"] = "esr",
        ["serum albumin"] = "albumin", ["total protein"] = "protein total",
        ["serum sodium"] = "sodium", ["na"] = "sodium",
        ["serum potassium"] = "potassium", ["k"] = "potassium",
        ["uric acid"] = "uric acid", ["serum uric acid"] = "uric acid"
    };

    /// <summary>Qualifiers that describe the specimen, not the analyte.</summary>
    [GeneratedRegex(@"\b(serum|plasma|blood|urine|s|p|b)\b[.\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex SpecimenPrefix();

    [GeneratedRegex(@"[^a-z0-9\s]", RegexOptions.IgnoreCase)]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Canonical grouping key, or null when there is no test name. An unrecognised test keeps its
    /// cleaned name — it still groups with itself across visits, which is what a trend needs.
    /// </summary>
    public static string? Standardize(string? testName)
    {
        if (string.IsNullOrWhiteSpace(testName)) return null;

        var cleaned = Punctuation().Replace(testName.Trim(), " ");
        cleaned = Whitespace().Replace(cleaned, " ").Trim().ToLowerInvariant();

        if (cleaned.Length == 0) return null;

        if (Aliases.TryGetValue(cleaned, out var direct)) return direct;

        // Retry after dropping a specimen prefix: "serum creatinine" → "creatinine".
        var withoutSpecimen = Whitespace().Replace(SpecimenPrefix().Replace(cleaned, " "), " ").Trim();

        if (withoutSpecimen.Length > 0 && Aliases.TryGetValue(withoutSpecimen, out var stripped))
        {
            return stripped;
        }

        return withoutSpecimen.Length > 0 ? withoutSpecimen : cleaned;
    }

    /// <summary>
    /// Whether a value falls outside the range printed on the document. Computed in code, never by
    /// the LLM (Principle 2), and only against a range the document itself supplied (FR-6.3).
    /// </summary>
    public static bool IsOutOfRange(decimal? value, decimal? min, decimal? max)
    {
        if (value is null) return false;
        if (min is null && max is null) return false;

        return (min is not null && value < min) || (max is not null && value > max);
    }
}
