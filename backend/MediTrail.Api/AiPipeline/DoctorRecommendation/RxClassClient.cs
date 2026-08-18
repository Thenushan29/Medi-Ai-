using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

public sealed record RxClassHit
{
    public required string ClassId { get; init; }
    public required string ClassName { get; init; }
    public required string ClassType { get; init; }
    public required string RelaSource { get; init; }
    public string? Rela { get; init; }
}

public sealed record RxClassLookup
{
    public required bool LookupFailed { get; init; }
    public IReadOnlyList<RxClassHit> Hits { get; init; } = [];

    public static RxClassLookup Miss() => new() { LookupFailed = false };
    public static RxClassLookup Failed() => new() { LookupFailed = true };
}

public interface IRxClassClient
{
    Task<RxClassLookup> MayTreatAsync(string drugName, CancellationToken ct = default);

    Task<RxClassLookup> AtcClassesAsync(string drugName, CancellationToken ct = default);
}

/// <summary>
/// NLM RxClass (RxNav). No API key. 20 requests/second. Does not call the discontinued
/// interaction API. Drug names are normalized before the request. A miss is not a failure.
/// </summary>
public sealed class RxClassClient(
    HttpClient http,
    IMemoryCache cache,
    IOptions<DoctorRecommendationOptions> options,
    ILogger<RxClassClient> logger) : IRxClassClient
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DoctorRecommendationOptions _options = options.Value;

    public Task<RxClassLookup> MayTreatAsync(string drugName, CancellationToken ct = default) =>
        LookupByDrugNameAsync(drugName, "MEDRT", "may_treat", allowSpelling: true, ct);

    public Task<RxClassLookup> AtcClassesAsync(string drugName, CancellationToken ct = default) =>
        LookupByDrugNameAsync(drugName, "ATC", rela: null, allowSpelling: false, ct);

    public static string? ToQueryName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DrugNameNormalizer.GenericForBrand(raw) ?? DrugNameNormalizer.Normalize(raw);
    }

    public static string ClassUrl(string source, string classId) =>
        $"https://mor.nlm.nih.gov/RxClass/search?searchBy=class&source={Uri.EscapeDataString(source)}&id={Uri.EscapeDataString(classId)}";

    private async Task<RxClassLookup> LookupByDrugNameAsync(
        string drugName, string relaSource, string? rela, bool allowSpelling, CancellationToken ct)
    {
        var query = ToQueryName(drugName);
        if (query is null) return RxClassLookup.Miss();

        var cacheKey = $"rxclass:{relaSource}:{rela}:{query}";
        if (cache.TryGetValue<RxClassLookup>(cacheKey, out var cached) && cached is not null)
            return cached;

        var lookup = await FetchByDrugNameAsync(query, relaSource, rela, ct);
        if (lookup.LookupFailed) return lookup;

        if (lookup.Hits.Count == 0 && allowSpelling)
        {
            var suggestion = await SuggestAsync(query, ct);
            if (suggestion is not null
                && !string.Equals(suggestion, query, StringComparison.OrdinalIgnoreCase))
            {
                lookup = await FetchByDrugNameAsync(suggestion, relaSource, rela, ct);
            }
        }

        if (!lookup.LookupFailed)
        {
            cache.Set(cacheKey, lookup, TimeSpan.FromHours(Math.Max(1, _options.CacheTtlHours)));
        }

        return lookup;
    }

    private async Task<RxClassLookup> FetchByDrugNameAsync(
        string query, string relaSource, string? rela, CancellationToken ct)
    {
        var url = $"rxclass/class/byDrugName.json?drugName={Uri.EscapeDataString(query)}&relaSource={relaSource}";
        if (!string.IsNullOrWhiteSpace(rela))
            url += $"&relas={rela}";

        return await GetClassesAsync(url, relaSource, ct);
    }

    private async Task<RxClassLookup> GetClassesAsync(string url, string relaSource, CancellationToken ct)
    {
        try
        {
            using var response = await SendWithRetryAsync(url, ct);
            if (response is null) return RxClassLookup.Failed();

            if (response.StatusCode == HttpStatusCode.NotFound)
                return RxClassLookup.Miss();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("RxClass returned {Status}", (int)response.StatusCode);
                return RxClassLookup.Failed();
            }

            var payload = await response.Content.ReadFromJsonAsync<RxClassResponse>(JsonOptions, ct);
            var hits = (payload?.List?.Info ?? [])
                .Select(row => row.Concept)
                .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.ClassName) && !string.IsNullOrWhiteSpace(c.ClassId))
                .Select(c => new RxClassHit
                {
                    ClassId = c!.ClassId!,
                    ClassName = c.ClassName!,
                    ClassType = c.ClassType ?? "",
                    RelaSource = relaSource,
                    Rela = null
                })
                .ToList();

            return new RxClassLookup { LookupFailed = false, Hits = hits };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RxClass lookup failed");
            return RxClassLookup.Failed();
        }
    }

    private async Task<string?> SuggestAsync(string term, CancellationToken ct)
    {
        try
        {
            var url = $"spellingsuggestions.json?term={Uri.EscapeDataString(term)}&type=DRUG";
            using var response = await SendWithRetryAsync(url, ct);
            if (response is null || !response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("suggestionGroup", out var group)
                || !group.TryGetProperty("suggestionList", out var list)
                || !list.TryGetProperty("suggestion", out var suggestion))
            {
                return null;
            }

            if (suggestion.ValueKind == JsonValueKind.String)
                return suggestion.GetString();

            if (suggestion.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in suggestion.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } first)
                        return first;
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RxClass spelling suggestion failed");
            return null;
        }
    }

    private async Task<HttpResponseMessage?> SendWithRetryAsync(string url, CancellationToken ct)
    {
        await ThrottleAsync(ct);
        var response = await http.GetAsync(url, ct);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return response;

        response.Dispose();
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        await ThrottleAsync(ct);
        return await http.GetAsync(url, ct);
    }

    private static async Task ThrottleAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _nextAllowed)
                await Task.Delay(_nextAllowed - now, ct);

            // NLM published cap is 20 requests/second.
            _nextAllowed = DateTimeOffset.UtcNow.AddMilliseconds(50);
        }
        finally
        {
            Gate.Release();
        }
    }

    private sealed record RxClassResponse
    {
        [JsonPropertyName("rxclassDrugInfoList")] public RxClassInfoList? List { get; init; }
    }

    private sealed record RxClassInfoList
    {
        [JsonPropertyName("rxclassDrugInfo")] public List<RxClassInfoRow>? Info { get; init; }
    }

    private sealed record RxClassInfoRow
    {
        [JsonPropertyName("rxclassMinConceptItem")] public RxClassConcept? Concept { get; init; }
    }

    private sealed record RxClassConcept
    {
        [JsonPropertyName("classId")] public string? ClassId { get; init; }
        [JsonPropertyName("className")] public string? ClassName { get; init; }
        [JsonPropertyName("classType")] public string? ClassType { get; init; }
    }
}
