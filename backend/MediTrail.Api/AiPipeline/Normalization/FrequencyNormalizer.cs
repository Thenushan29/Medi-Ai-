using System.Text.RegularExpressions;

namespace MediTrail.Api.AiPipeline.Normalization;

/// <summary>
/// Turns printed dosing instructions into doses per day (FR-4.4), so a dosage conflict can be
/// detected by comparing numbers rather than by asking a model to compare strings.
///
/// Arithmetic is code, never the LLM (Principle 2). The model still reports its own
/// <c>frequencyPerDay</c>; this recomputes from the printed text and is preferred where the two
/// disagree, because a regex over "1-0-1" cannot hallucinate.
/// </summary>
public static partial class FrequencyNormalizer
{
    /// <summary>Latin abbreviations, as they actually appear on prescriptions.</summary>
    private static readonly Dictionary<string, decimal> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["od"] = 1, ["qd"] = 1, ["daily"] = 1, ["once daily"] = 1, ["nocte"] = 1, ["mane"] = 1,
        ["hs"] = 1, ["om"] = 1, ["on"] = 1,
        ["bd"] = 2, ["bid"] = 2, ["twice daily"] = 2, ["twice a day"] = 2, ["b.d"] = 2, ["b.i.d"] = 2,
        ["tds"] = 3, ["tid"] = 3, ["thrice daily"] = 3, ["three times daily"] = 3,
        ["t.d.s"] = 3, ["t.i.d"] = 3,
        ["qds"] = 4, ["qid"] = 4, ["four times daily"] = 4, ["q.i.d"] = 4, ["q.d.s"] = 4
    };

    /// <summary>"1-0-1" and "1-1-1" — morning-afternoon-night, common on South Asian prescriptions.</summary>
    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?|½|1/2)\s*-\s*(\d+(?:\.\d+)?|½|1/2)\s*-\s*(\d+(?:\.\d+)?|½|1/2)(\s*-\s*(\d+(?:\.\d+)?|½|1/2))?\s*$")]
    private static partial Regex SlotPattern();

    /// <summary>"1 Morning, 1 Night", "1 सुबह, 1 रात" — count the time-of-day mentions.</summary>
    [GeneratedRegex(@"\b(morning|afternoon|evening|night|noon|aft|eve|bedtime)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TimeOfDay();

    /// <summary>"every 6 hours", "q6h", "6 hourly".</summary>
    [GeneratedRegex(@"(?:every\s+|q\s*)(\d+(?:\.\d+)?)\s*(?:h|hr|hrs|hour|hours|hourly)", RegexOptions.IgnoreCase)]
    private static partial Regex EveryHours();

    /// <summary>"3 times a day", "2 times daily".</summary>
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:x|times?)\s*(?:a\s+|per\s+)?(?:day|daily)", RegexOptions.IgnoreCase)]
    private static partial Regex TimesPerDay();

    /// <summary>As-needed dosing has no fixed rate — null, not a guessed number.</summary>
    [GeneratedRegex(@"\b(prn|sos|as\s+needed|as\s+required|when\s+required|if\s+needed|when\s+necessary)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsNeeded();

    /// <summary>Weekly, alternate-day and monthly dosing do not reduce to a daily count here.</summary>
    [GeneratedRegex(@"\b(weekly|fortnight|monthly|alternate\s+day|every\s+other\s+day|once\s+a\s+week|stat)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotDaily();

    /// <summary>
    /// Doses per day, or null when the text has no fixed daily rate. Null is a correct answer —
    /// a PRN medication genuinely has no rate, and inventing one would create a false dosage conflict.
    /// </summary>
    public static decimal? PerDay(string? frequency)
    {
        if (string.IsNullOrWhiteSpace(frequency)) return null;

        var text = frequency.Trim();

        if (AsNeeded().IsMatch(text)) return null;
        if (NotDaily().IsMatch(text)) return null;

        // "1-0-1" style slots — sum them.
        var slots = SlotPattern().Match(text);
        if (slots.Success)
        {
            var total = 0m;
            for (var i = 1; i < slots.Groups.Count; i++)
            {
                var group = slots.Groups[i];
                if (group.Success && ParseQuantity(group.Value) is { } quantity) total += quantity;
            }
            return total > 0 ? total : null;
        }

        var everyHours = EveryHours().Match(text);
        if (everyHours.Success && decimal.TryParse(everyHours.Groups[1].Value, out var hours) && hours > 0)
        {
            return Math.Round(24m / hours, 2);
        }

        var timesPerDay = TimesPerDay().Match(text);
        if (timesPerDay.Success && decimal.TryParse(timesPerDay.Groups[1].Value, out var times) && times > 0)
        {
            return times;
        }

        // Longest abbreviation first, so "b.i.d" is not matched as "on".
        foreach (var (abbreviation, perDay) in Abbreviations.OrderByDescending(kv => kv.Key.Length))
        {
            if (ContainsToken(text, abbreviation)) return perDay;
        }

        // "1 Morning, 1 Night" — distinct times of day, counted.
        var mentions = TimeOfDay().Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Select(v => v switch { "aft" => "afternoon", "eve" => "evening", "bedtime" => "night", _ => v })
            .Distinct()
            .Count();

        return mentions > 0 ? mentions : null;
    }

    private static decimal? ParseQuantity(string value) => value switch
    {
        "½" or "1/2" => 0.5m,
        _ => decimal.TryParse(value, out var parsed) ? parsed : null
    };

    /// <summary>Whole-token match, so "od" does not fire inside "food".</summary>
    private static bool ContainsToken(string text, string token)
    {
        var pattern = $@"(^|[^a-z0-9]){Regex.Escape(token)}($|[^a-z0-9])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }
}
