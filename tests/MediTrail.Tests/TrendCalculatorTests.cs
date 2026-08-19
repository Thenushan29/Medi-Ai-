using MediTrail.Api.AiPipeline.Trends;

namespace MediTrail.Tests;

/// <summary>
/// Trend arithmetic. This is computed in code precisely so it can be pinned down like this —
/// the model only writes the sentence describing the result (Principle 2).
/// </summary>
public class TrendCalculatorTests
{
    [Fact]
    public void RefusesADirectionWithFewerThanThreePoints() =>
        // FR-6.4. Two points is a line, not a trend.
        Assert.Equal(TrendDirection.Insufficient, Build([(2021, 40m), (2023, 88m)]).Direction);

    [Fact]
    public void DetectsARisingSeries()
    {
        var series = Build([(2021, 40m), (2022, 62m), (2023, 88m)]);

        Assert.Equal(TrendDirection.Rising, series.Direction);
        Assert.Equal(120m, series.PercentChange);
    }

    [Fact]
    public void DetectsAFallingSeries() =>
        Assert.Equal(TrendDirection.Falling, Build([(2021, 88m), (2022, 60m), (2023, 40m)]).Direction);

    [Fact]
    public void TreatsSmallMovementAsStable() =>
        // Under the 10% threshold: noise, not drift.
        Assert.Equal(TrendDirection.Stable, Build([(2021, 50m), (2022, 52m), (2023, 53m)]).Direction);

    [Fact]
    public void DoesNotCallAnUpAndDownSeriesATrend()
    {
        // Net change is large, but the movement is not consistent. Calling this a trend would
        // invent a story the data does not tell.
        var series = Build([(2020, 40m), (2021, 90m), (2022, 35m), (2023, 95m)]);

        Assert.Equal(TrendDirection.Stable, series.Direction);
    }

    [Fact]
    public void OrdersPointsByDateRegardlessOfInputOrder()
    {
        var series = Build([(2023, 88m), (2021, 40m), (2022, 62m)]);

        Assert.Equal([40m, 62m, 88m], series.Points.Select(p => p.Value));
        Assert.Equal(TrendDirection.Rising, series.Direction);
    }

    [Fact]
    public void ReportsOutOfRangeCountAndWhetherTheLatestIsOutside()
    {
        var series = TrendCalculator.Build("alt", "SGPT (ALT)", "U/L", 7, 56, "7 - 56",
        [
            new TrendPoint(new DateOnly(2021, 1, 1), 40m, false, Guid.NewGuid()),
            new TrendPoint(new DateOnly(2022, 1, 1), 62m, true, Guid.NewGuid()),
            new TrendPoint(new DateOnly(2023, 1, 1), 88m, true, Guid.NewGuid())
        ]);

        Assert.Equal(2, series.OutOfRangeCount);
        Assert.True(series.LatestOutOfRange);
    }

    [Fact]
    public void HandlesASingleReading()
    {
        var series = Build([(2023, 88m)]);

        Assert.Equal(TrendDirection.Insufficient, series.Direction);
        Assert.Null(series.PercentChange);
        Assert.Single(series.Points);
    }

    [Fact]
    public void DoesNotDivideByZeroOnAZeroFirstReading()
    {
        var series = Build([(2021, 0m), (2022, 5m), (2023, 10m)]);

        Assert.Null(series.PercentChange);
        Assert.Equal(TrendDirection.Stable, series.Direction);
    }

    [Fact]
    public void SupplementaryHba1cSeriesIsRising()
    {
        // dataset/supplementary demo reports: 6.4 → 7.1 → 8.2. Locked so the Lab Trends
        // demo cannot silently stop being a trend if the threshold changes.
        var series = TrendCalculator.Build("hba1c", "HbA1c", "%", 4.0m, 5.6m, "4.0 - 5.6",
        [
            new TrendPoint(new DateOnly(2022, 3, 15), 6.4m, true, Guid.NewGuid()),
            new TrendPoint(new DateOnly(2023, 4, 10), 7.1m, true, Guid.NewGuid()),
            new TrendPoint(new DateOnly(2024, 6, 2), 8.2m, true, Guid.NewGuid())
        ]);

        Assert.Equal(TrendDirection.Rising, series.Direction);
        Assert.True(series.LatestOutOfRange);
        Assert.Equal(3, series.OutOfRangeCount);
    }

    private static TrendSeries Build((int Year, decimal Value)[] points) =>
        TrendCalculator.Build("alt", "SGPT (ALT)", "U/L", 7, 56, "7 - 56",
            points.Select(p => new TrendPoint(new DateOnly(p.Year, 1, 1), p.Value, false, Guid.NewGuid())));
}
