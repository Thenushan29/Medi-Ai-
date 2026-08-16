using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediTrail.Api.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline.Verification;

/// <summary>
/// Stage 5 of the pipeline (§11.1): independent corroboration of an AI finding against the openFDA
/// drug label database.
///
/// The contract that matters is what happens on failure. A finding is **never** removed because
/// openFDA could not confirm it (FR-5.7) — absence of confirmation is not evidence of safety
/// (§28.1). Not-found is a normal result, not an error (§16.2).
/// </summary>
public interface IOpenFdaClient
{
    /// <summary>
    /// Looks for evidence that <paramref name="genericName"/>'s label mentions
    /// <paramref name="interactsWith"/>. Returns an unverified result rather than throwing.
    /// </summary>
    Task<FdaVerification> VerifyInteractionAsync(
        string genericName, string interactsWith, CancellationToken ct = default);

    /// <summary>Whether openFDA recognises the generic at all — used to adjust confidence (§11.4).</summary>
    Task<bool> GenericExistsAsync(string genericName, CancellationToken ct = default);
}

public sealed record FdaVerification
{
    public required bool Confirmed { get; init; }

    /// <summary>Short attributed excerpt. Never reproduced at length (§16.2).</summary>
    public string? Excerpt { get; init; }

    public string? Source { get; init; }

    /// <summary>True when the lookup itself failed, as distinct from a genuine not-found.</summary>
    public required bool LookupFailed { get; init; }

    public static FdaVerification NotConfirmed() => new() { Confirmed = false, LookupFailed = false };
    public static FdaVerification Failed() => new() { Confirmed = false, LookupFailed = true };
}

public sealed class OpenFdaClient(
    HttpClient http,
    IMemoryCache cache,
    IOptions<OpenFdaOptions> options,
    ILogger<OpenFdaClient> logger) : IOpenFdaClient
{
    private readonly OpenFdaOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Label sections worth searching for an interaction mention.</summary>
    private static readonly string[] Sections =
        ["drug_interactions", "warnings", "contraindications", "warnings_and_cautions", "boxed_warning"];

    public async Task<FdaVerification> VerifyInteractionAsync(
        string genericName, string interactsWith, CancellationToken ct = default)
    {
        var label = await GetLabelAsync(genericName, ct);

        if (label is null) return FdaVerification.Failed();
        if (label.NotFound) return FdaVerification.NotConfirmed();

        // Matching on the other drug's name inside the label's own interaction text. A hit is
        // real corroboration; a miss only means this label does not mention it.
        var needle = interactsWith.Trim();

        foreach (var section in label.Sections)
        {
            var index = section.Value.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            return new FdaVerification
            {
                Confirmed = true,
                Excerpt = Excerpt(section.Value, index),
                Source = $"FDA drug label ({section.Key.Replace('_', ' ')}) — openFDA",
                LookupFailed = false
            };
        }

        return FdaVerification.NotConfirmed();
    }

    public async Task<bool> GenericExistsAsync(string genericName, CancellationToken ct = default)
    {
        var label = await GetLabelAsync(genericName, ct);
        return label is not null && !label.NotFound;
    }

    /// <summary>
    /// One network call per generic, cached (§11.6). Queried by **generic name only** — the label
    /// database will not resolve regional brand names (§16.2), and asking it to would produce
    /// confident nonsense.
    /// </summary>
    private async Task<LabelResult?> GetLabelAsync(string genericName, CancellationToken ct)
    {
        var key = $"openfda:{genericName.ToLowerInvariant()}";

        if (cache.TryGetValue<LabelResult>(key, out var cached)) return cached;

        try
        {
            var query = $"openfda.generic_name:\"{Uri.EscapeDataString(genericName)}\"";
            var url = $"/drug/label.json?search={query}&limit=1";

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                url += $"&api_key={_options.ApiKey}";
            }

            var response = await http.GetAsync(url, ct);

            // openFDA answers an unmatched search with 404. That is a normal result (§16.2),
            // and worth caching so a drug it does not know is asked about once.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var miss = new LabelResult { NotFound = true, Sections = [] };
                cache.Set(key, miss, TimeSpan.FromHours(_options.CacheHours));
                return miss;
            }

            if (!response.IsSuccessStatusCode)
            {
                // The status is the operational signal and carries nothing about the patient.
                // The drug being looked up is medication data, so it stays at Debug.
                logger.LogWarning("openFDA label lookup returned {Status}", (int)response.StatusCode);
                logger.LogDebug("openFDA returned {Status} for {Generic}",
                    (int)response.StatusCode, genericName);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<FdaResponse>(JsonOptions, ct);
            var record = payload?.Results?.FirstOrDefault();

            if (record is null)
            {
                var miss = new LabelResult { NotFound = true, Sections = [] };
                cache.Set(key, miss, TimeSpan.FromHours(_options.CacheHours));
                return miss;
            }

            var sections = new Dictionary<string, string>();
            foreach (var name in Sections)
            {
                if (record.TryGetSection(name, out var text)) sections[name] = text;
            }

            var result = new LabelResult { NotFound = false, Sections = sections };
            cache.Set(key, result, TimeSpan.FromHours(_options.CacheHours));
            return result;
        }
        catch (Exception ex)
        {
            // Never a hard dependency (§14.4). The caller marks the finding unverified and moves on.
            logger.LogWarning(ex, "openFDA lookup failed");
            logger.LogDebug(ex, "openFDA lookup failed for {Generic}", genericName);
            return null;
        }
    }

    /// <summary>
    /// A short window around the match, snapped to sentence boundaries. Deliberately brief —
    /// §16.2 permits an attributed excerpt, not republication of the label.
    /// </summary>
    private static string Excerpt(string text, int index)
    {
        const int Before = 120;
        const int After = 260;

        var start = Math.Max(0, index - Before);
        var end = Math.Min(text.Length, index + After);

        var slice = text[start..end].Trim();

        if (start > 0)
        {
            var sentence = slice.IndexOf(". ", StringComparison.Ordinal);
            if (sentence >= 0 && sentence < 80) slice = slice[(sentence + 2)..];
            slice = "…" + slice;
        }

        if (end < text.Length) slice += "…";

        return slice;
    }

    private sealed record LabelResult
    {
        public required bool NotFound { get; init; }
        public required Dictionary<string, string> Sections { get; init; }
    }

    private sealed record FdaResponse
    {
        [JsonPropertyName("results")] public List<FdaRecord>? Results { get; init; }
    }

    private sealed record FdaRecord
    {
        [JsonPropertyName("drug_interactions")] public List<string>? DrugInteractions { get; init; }
        [JsonPropertyName("warnings")] public List<string>? Warnings { get; init; }
        [JsonPropertyName("contraindications")] public List<string>? Contraindications { get; init; }
        [JsonPropertyName("warnings_and_cautions")] public List<string>? WarningsAndCautions { get; init; }
        [JsonPropertyName("boxed_warning")] public List<string>? BoxedWarning { get; init; }

        public bool TryGetSection(string name, out string text)
        {
            var values = name switch
            {
                "drug_interactions" => DrugInteractions,
                "warnings" => Warnings,
                "contraindications" => Contraindications,
                "warnings_and_cautions" => WarningsAndCautions,
                "boxed_warning" => BoxedWarning,
                _ => null
            };

            text = values is null ? string.Empty : string.Join(" ", values);
            return text.Length > 0;
        }
    }
}
