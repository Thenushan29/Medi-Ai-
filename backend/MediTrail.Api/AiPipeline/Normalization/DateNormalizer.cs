using System.Globalization;
using System.Text.RegularExpressions;

namespace MediTrail.Api.AiPipeline.Normalization;

/// <summary>
/// Parses printed dates to ISO, returning null on ambiguity (FR-4.1).
///
/// The dataset makes this load-bearing: one document prints the placeholder year <c>20yy</c>, and
/// several use <c>dd-mm-yy</c> with no format hint. A wrong year silently reorders the whole
/// timeline, so an unparseable date must stay null rather than become a plausible guess.
/// </summary>
public static partial class DateNormalizer
{
    /// <summary>Formats that cannot be misread — the day or month is named, or the year leads.</summary>
    private static readonly string[] UnambiguousFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd",
        "dd-MMM-yyyy", "d-MMM-yyyy", "dd MMM yyyy", "d MMM yyyy",
        "MMM dd, yyyy", "MMM d, yyyy", "MMMM dd, yyyy", "MMMM d, yyyy",
        "dd-MMMM-yyyy", "d MMMM yyyy",
        "dddd MMMM dd, yyyy", "dddd MMM dd, yyyy"
    ];

    /// <summary>Three numeric parts separated by / - or . — the ambiguous shape.</summary>
    [GeneratedRegex(@"^(\d{1,4})[-/.](\d{1,2})[-/.](\d{2,4})$")]
    private static partial Regex NumericDate();

    /// <summary>A placeholder year such as `20yy` or `20XX`. Never a real date.</summary>
    [GeneratedRegex(@"\b(19|20)\s*(yy|xx|__)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderYear();

    /// <summary>Strips a trailing time so "27-Apr-2020, 04:37 PM" still parses.</summary>
    [GeneratedRegex(@"[,\s]+\d{1,2}:\d{2}(:\d{2})?\s*(am|pm)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingTime();

    /// <summary>
    /// Returns an ISO date, or null when the text is absent, a placeholder, or genuinely ambiguous.
    /// </summary>
    public static DateOnly? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = TrailingTime().Replace(text.Trim(), string.Empty).Trim();

        if (cleaned.Length == 0) return null;
        if (PlaceholderYear().IsMatch(cleaned)) return null;

        foreach (var format in UnambiguousFormats)
        {
            if (DateOnly.TryParseExact(cleaned, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var exact))
            {
                return exact;
            }
        }

        var numeric = NumericDate().Match(cleaned);
        if (numeric.Success) return ResolveNumeric(numeric);

        return null;
    }

    /// <summary>
    /// Resolves a numeric date only when the arrangement is forced. `13/04/2022` can only be
    /// day-first; `01/11/2025` cannot be decided and stays null.
    /// </summary>
    private static DateOnly? ResolveNumeric(Match match)
    {
        var first = int.Parse(match.Groups[1].Value);
        var second = int.Parse(match.Groups[2].Value);
        var thirdText = match.Groups[3].Value;

        // yyyy-mm-dd caught above; a four-digit lead here is still year-first.
        if (match.Groups[1].Value.Length == 4)
        {
            return Build(first, second, int.Parse(thirdText));
        }

        // A two-digit year is not resolvable to a century with confidence. `09-11-12` could be
        // 2012, 2009, or 1912 — and it is also day/month ambiguous. Null on both counts.
        if (thirdText.Length != 4) return null;

        var year = int.Parse(thirdText);

        var firstCanBeMonth = first is >= 1 and <= 12;
        var secondCanBeMonth = second is >= 1 and <= 12;

        return (firstCanBeMonth, secondCanBeMonth) switch
        {
            // 04/13/2022 — the second part exceeds 12, so it must be the day: month-first.
            (true, false) => Build(year, month: first, day: second),
            // 13/04/2022 — the first part exceeds 12, so it must be the day: day-first.
            (false, true) => Build(year, month: second, day: first),
            // 01/11/2025 — both parts could be either. Undecidable without a format hint (§11.3).
            (true, true) => null,
            // Neither can be a month: not a date.
            _ => null
        };
    }

    private static DateOnly? Build(int year, int month, int day)
    {
        if (month is < 1 or > 12) return null;
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return null;

        // A record dated far in the future is a misread, not a prescription.
        var date = new DateOnly(year, month, day);
        return date > DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)) ? null : date;
    }
}
