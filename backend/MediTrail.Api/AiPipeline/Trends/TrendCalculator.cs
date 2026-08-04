namespace MediTrail.Api.AiPipeline.Trends;

public enum TrendDirection
{
    /// <summary>Fewer than three points — a direction cannot be claimed (FR-6.4).</summary>
    Insufficient,
    Rising,
    Falling,
    Stable
}

public sealed record TrendPoint(DateOnly Date, decimal Value, bool IsOutOfRange, Guid DocumentId);

public sealed record TrendSeries
{
    public required string TestKey { get; init; }
    public required string DisplayName { get; init; }
    public string? Unit { get; init; }
    public decimal? NormalMin { get; init; }
    public decimal? NormalMax { get; init; }
    public string? NormalRangeText { get; init; }
    public required IReadOnlyList<TrendPoint> Points { get; init; }
    public required TrendDirection Direction { get; init; }

    /// <summary>Percentage change from first to last point, or null when it cannot be computed.</summary>
    public decimal? PercentChange { get; init; }

    public required int OutOfRangeCount { get; init; }

    /// <summary>True when the latest value sits outside the range the document printed.</summary>
    public required bool LatestOutOfRange { get; init; }
}

/// <summary>
/// Stage 6, the arithmetic half (§11.1): series and drift computed in code, never by the LLM
/// (Principle 2 — "never use an LLM for arithmetic"). The model is only asked to put the result
/// into a sentence, and it is given the numbers rather than asked to derive them.
/// </summary>
public static class TrendCalculator
{
    /// <summary>Below this, a change is noise rather than drift.</summary>
    private const decimal DriftThresholdPercent = 10m;

    /// <summary>FR-6.4: a direction needs at least three points.</summary>
    private const int MinimumPointsForDirection = 3;

    public static TrendSeries Build(
        string testKey,
        string displayName,
        string? unit,
        decimal? normalMin,
        decimal? normalMax,
        string? normalRangeText,
        IEnumerable<TrendPoint> points)
    {
        var ordered = points.OrderBy(p => p.Date).ToList();

        var direction = TrendDirection.Insufficient;
        decimal? percentChange = null;

        if (ordered.Count >= MinimumPointsForDirection)
        {
            var first = ordered[0].Value;
            var last = ordered[^1].Value;

            if (first != 0)
            {
                percentChange = Math.Round((last - first) / Math.Abs(first) * 100m, 1);
            }

            direction = Classify(ordered, percentChange);
        }
        else if (ordered.Count == 2 && ordered[0].Value != 0)
        {
            // Reported for context, but not enough to call a direction.
            percentChange = Math.Round((ordered[1].Value - ordered[0].Value) / Math.Abs(ordered[0].Value) * 100m, 1);
        }

        return new TrendSeries
        {
            TestKey = testKey,
            DisplayName = displayName,
            Unit = unit,
            NormalMin = normalMin,
            NormalMax = normalMax,
            NormalRangeText = normalRangeText,
            Points = ordered,
            Direction = direction,
            PercentChange = percentChange,
            OutOfRangeCount = ordered.Count(p => p.IsOutOfRange),
            LatestOutOfRange = ordered.Count > 0 && ordered[^1].IsOutOfRange
        };
    }

    /// <summary>
    /// How much of the total movement has to be in one direction before it counts as drift.
    /// A value that swings up and down has not drifted, however far apart its endpoints happen
    /// to land, and calling that a trend would tell the reader a story the data does not support.
    /// </summary>
    private const decimal MinimumDirectionality = 0.6m;

    /// <summary>
    /// Direction needs a meaningful net change **and** movement that is mostly one-way.
    ///
    /// Counting rising versus falling steps is not enough: 40 → 90 → 35 → 95 has two rises to one
    /// fall and ends far above where it started, yet it is plainly noise. Comparing the net change
    /// against the total distance travelled catches that — the ratio there is 0.33.
    /// </summary>
    private static TrendDirection Classify(List<TrendPoint> ordered, decimal? percentChange)
    {
        if (percentChange is null) return TrendDirection.Stable;
        if (Math.Abs(percentChange.Value) < DriftThresholdPercent) return TrendDirection.Stable;

        var net = ordered[^1].Value - ordered[0].Value;

        var travelled = 0m;
        for (var i = 1; i < ordered.Count; i++)
        {
            travelled += Math.Abs(ordered[i].Value - ordered[i - 1].Value);
        }

        if (travelled == 0) return TrendDirection.Stable;

        var directionality = Math.Abs(net) / travelled;
        if (directionality < MinimumDirectionality) return TrendDirection.Stable;

        return net > 0 ? TrendDirection.Rising : TrendDirection.Falling;
    }
}
