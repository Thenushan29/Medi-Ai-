using System.Globalization;
using System.Text.RegularExpressions;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

/// <summary>
/// Heuristic only — not a full OSM opening_hours parser. Missing hours stay unknown.
/// </summary>
public static partial class AvailabilityMatcher
{
    public const string Match = "match";
    public const string Unknown = "unknown";
    public const string Indeterminate = "indeterminate";
    public const string NoMatch = "no_match";

    public static string MatchRequest(string? openingHours, string? availability)
    {
        var window = string.IsNullOrWhiteSpace(availability) ? "anytime" : availability.Trim().ToLowerInvariant();
        var hours = openingHours?.Trim();

        if (string.IsNullOrWhiteSpace(hours)) return Unknown;

        if (IsAlwaysOpen(hours)) return Match;

        if (window is "anytime" or "this_week") return Match;

        return window switch
        {
            "evenings" => MatchesEvenings(hours) ? Match : HasClockTimes(hours) ? NoMatch : Indeterminate,
            "weekend" => MatchesWeekend(hours) ? Match : NoMatch,
            _ => Indeterminate
        };
    }

    public static int Score(string availabilityMatch) => availabilityMatch switch
    {
        Match => 10,
        Indeterminate => 4,
        _ => 0
    };

    private static bool IsAlwaysOpen(string hours) =>
        hours.Contains("24/7", StringComparison.OrdinalIgnoreCase)
        || hours.Contains("24-7", StringComparison.OrdinalIgnoreCase)
        || hours.Contains("24 hours", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesWeekend(string hours) => WeekendToken().IsMatch(hours);

    private static bool MatchesEvenings(string hours)
    {
        foreach (Match range in HoursRange().Matches(hours))
        {
            if (!int.TryParse(range.Groups["close"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var closeHour))
                continue;
            if (!int.TryParse(range.Groups["open"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var openHour))
                continue;

            if (closeHour >= 18) return true;
            // Overnight range, e.g. 20:00-02:00.
            if (closeHour < openHour) return true;
        }

        return false;
    }

    private static bool HasClockTimes(string hours) => HoursRange().IsMatch(hours);

    [GeneratedRegex(@"\b(Sa|Su|Sat|Sun|Saturday|Sunday|weekend)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeekendToken();

    [GeneratedRegex(@"(?<open>\d{1,2}):(?<omin>\d{2})\s*-\s*(?<close>\d{1,2}):(?<cmin>\d{2})")]
    private static partial Regex HoursRange();
}
