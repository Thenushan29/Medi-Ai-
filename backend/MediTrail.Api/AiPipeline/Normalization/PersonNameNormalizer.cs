using System.Text.RegularExpressions;

namespace MediTrail.Api.AiPipeline.Normalization;

/// <summary>
/// Rejects a name that is really a redaction.
///
/// The evaluation documents have the prescriber's name covered by a black bar, and a vision model
/// reports the letters that survive at its edge — "Dr. Ak", "Dr. C". The interface then presents
/// that as the doctor who wrote the prescription, which is a name the system invented from a
/// censored field. Exactly the confident-but-wrong output Principle 1 forbids, and the prompt alone
/// does not reliably prevent it.
///
/// Deterministic, so it cannot be talked out of it.
/// </summary>
public static partial class PersonNameNormalizer
{
    /// <summary>Honorifics and qualifications, which are not the name itself.</summary>
    [GeneratedRegex(@"\b(dr|doctor|mr|mrs|ms|miss|prof|professor|md|mbbs|ms|mch|dnb|frcs|mrcp|phd|bds|do)\b\.?",
        RegexOptions.IgnoreCase)]
    private static partial Regex Honorific();

    /// <summary>Glyphs a model emits when transcribing a censor bar.</summary>
    [GeneratedRegex(@"[█▓▒░■▪●◼⬛�]")]
    private static partial Regex RedactionGlyph();

    [GeneratedRegex(@"[^\p{L}\p{M}\s]")]
    private static partial Regex NonLetter();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Returns the name, or null when what remains cannot be one.
    ///
    /// A surname of one or two letters is not a short name, it is the start of a longer one that
    /// was covered up. Reporting nothing is honest; reporting "Dr. Ak" is not.
    /// </summary>
    public static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var value = name.Trim();

        // Any censor glyph at all means the field was obscured, whatever else survived.
        if (RedactionGlyph().IsMatch(value)) return null;

        // What is left once titles and punctuation are removed is the name we are judging.
        var bare = Honorific().Replace(value, " ");
        bare = NonLetter().Replace(bare, " ");
        bare = Whitespace().Replace(bare, " ").Trim();

        // "Ak", "C", "O" — a fragment, not a name.
        return bare.Length < 3 ? null : value;
    }
}
