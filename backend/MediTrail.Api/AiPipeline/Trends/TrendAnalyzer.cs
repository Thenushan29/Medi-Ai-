using System.Globalization;
using System.Text.Json.Serialization;
using MediTrail.Api.AiPipeline.Extraction;
using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.AiPipeline.Trends;

/// <summary>
/// Stage 6 (§11.1): one series per standardized test, with a plain-language explanation.
///
/// The split is deliberate — <see cref="TrendCalculator"/> does the arithmetic, the model only
/// writes the sentence, and it is handed the computed numbers rather than asked to derive them
/// (Principle 2). If the model is unavailable the series still render; only the sentence is missing.
/// </summary>
public interface ITrendAnalyzer
{
    Task<IReadOnlyList<LabTrendDto>> AnalyzeAsync(Guid patientId, CancellationToken ct = default);
}

public sealed class TrendAnalyzer(
    MediTrailDbContext db,
    IPromptLibrary prompts,
    IServiceProvider services,
    ILogger<TrendAnalyzer> logger) : ITrendAnalyzer
{
    public async Task<IReadOnlyList<LabTrendDto>> AnalyzeAsync(Guid patientId, CancellationToken ct = default)
    {
        var results = await db.LabResults
            .AsNoTracking()
            .Where(l => l.PatientId == patientId
                     && l.TestNameStandard != null
                     && l.ValueNumeric != null
                     && l.TestDate != null)
            .ToListAsync(ct);

        if (results.Count == 0) return [];

        var trends = new List<LabTrendDto>();
        var ai = services.GetService<IAiClient>();

        foreach (var group in results.GroupBy(l => l.TestNameStandard!).OrderBy(g => g.Key))
        {
            var items = group.ToList();

            var series = TrendCalculator.Build(
                testKey: group.Key,
                // The name as printed on the most recent report reads better than the internal key.
                displayName: items.OrderByDescending(l => l.TestDate).First().TestName ?? group.Key,
                unit: items.Select(l => l.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                normalMin: items.Select(l => l.NormalMin).FirstOrDefault(v => v is not null),
                normalMax: items.Select(l => l.NormalMax).FirstOrDefault(v => v is not null),
                normalRangeText: items.Select(l => l.NormalRangeText).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
                points: items.Select(l => new TrendPoint(
                    l.TestDate!.Value, l.ValueNumeric!.Value, l.IsOutOfRange, l.DocumentId)));

            var explanation = ai is null ? null : await ExplainAsync(ai, series, ct);

            trends.Add(new LabTrendDto
            {
                TestKey = series.TestKey,
                DisplayName = series.DisplayName,
                Unit = series.Unit,
                NormalMin = series.NormalMin,
                NormalMax = series.NormalMax,
                NormalRangeText = series.NormalRangeText,
                Direction = series.Direction.ToString(),
                PercentChange = series.PercentChange,
                OutOfRangeCount = series.OutOfRangeCount,
                LatestOutOfRange = series.LatestOutOfRange,
                Points = series.Points.Select(p => new LabTrendPointDto
                {
                    Date = p.Date,
                    Value = p.Value,
                    IsOutOfRange = p.IsOutOfRange,
                    DocumentId = p.DocumentId
                }).ToList(),
                ExplanationEn = explanation?.ExplanationEn ?? Fallback(series),
                ExplanationTa = explanation?.ExplanationTa,
                Confidence = explanation?.Confidence ?? 100
            });
        }

        return trends;
    }

    private async Task<TrendExplanation?> ExplainAsync(IAiClient ai, TrendSeries series, CancellationToken ct)
    {
        try
        {
            var prompt = prompts.Get("trend", new Dictionary<string, string>
            {
                ["TEST_NAME"] = series.DisplayName,
                ["UNIT"] = series.Unit ?? "not stated",
                ["RANGE"] = series.NormalRangeText
                    ?? (series.NormalMin is null && series.NormalMax is null
                        ? "not printed on the report"
                        : $"{series.NormalMin} to {series.NormalMax}"),
                ["SERIES"] = string.Join(", ", series.Points.Select(p =>
                    $"{p.Date:yyyy-MM-dd}: {p.Value.ToString(CultureInfo.InvariantCulture)}")),
                ["DIRECTION"] = series.Direction.ToString(),
                ["CHANGE"] = series.PercentChange is null
                    ? "not computable"
                    : $"{series.PercentChange}%",
                ["OUT_OF_RANGE"] = series.OutOfRangeCount.ToString(),
                ["TOTAL"] = series.Points.Count.ToString()
            });

            var completion = await ai.CompleteAsync(prompt, "Explain this trend.", ct: ct);

            if (JsonResponseReader.TryRead<TrendExplanation>(completion.Content, out var explanation, out var error))
            {
                return explanation;
            }

            logger.LogWarning("Trend explanation unusable for {Test}: {Error}", series.TestKey, error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing sentence must not cost the user the chart (§14.4).
            logger.LogWarning(ex, "Trend explanation unavailable for {Test}", series.TestKey);
        }

        return null;
    }

    /// <summary>
    /// Used when the model is unavailable. Says only what the arithmetic already established —
    /// no interpretation, so it is safe to show unattended.
    /// </summary>
    private static string Fallback(TrendSeries series)
    {
        var latest = series.Points.Count > 0 ? series.Points[^1] : null;

        var reading = latest is null
            ? string.Empty
            : $"The most recent reading was {latest.Value}{Unit(series)} on {latest.Date:d MMMM yyyy}. ";

        var movement = series.Direction switch
        {
            TrendDirection.Rising => $"Across {series.Points.Count} readings this has been rising" +
                                     Change(series) + ". ",
            TrendDirection.Falling => $"Across {series.Points.Count} readings this has been falling" +
                                      Change(series) + ". ",
            TrendDirection.Stable => $"Across {series.Points.Count} readings this has stayed broadly level. ",
            _ => "There are not enough readings yet to show a trend. "
        };

        var range = series.LatestOutOfRange
            ? "The latest value is outside the range printed on the report."
            : series.NormalRangeText is null && series.NormalMin is null
                ? "No reference range was printed on the report."
                : "The latest value is inside the range printed on the report.";

        return reading + movement + range;
    }

    private static string Change(TrendSeries series) =>
        series.PercentChange is null ? string.Empty : $" ({series.PercentChange:+0.#;-0.#}%)";

    private static string Unit(TrendSeries series) =>
        string.IsNullOrWhiteSpace(series.Unit) ? string.Empty : " " + series.Unit;

    private sealed record TrendExplanation
    {
        [JsonPropertyName("explanationEn")] public string? ExplanationEn { get; init; }
        [JsonPropertyName("explanationTa")] public string? ExplanationTa { get; init; }
        [JsonPropertyName("confidence")] public int Confidence { get; init; } = 70;
    }
}
