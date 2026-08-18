using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediTrail.Api.Configuration;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

public interface IProviderCache
{
    Task<ProviderResult?> TryGetAsync(string keyPrefix, ProviderQuery query, CancellationToken ct = default);

    Task SetAsync(string keyPrefix, ProviderQuery query, ProviderResult result, CancellationToken ct = default);
}

/// <summary>
/// Read-through store for provider payloads. Cached rows keep the original
/// <see cref="ProviderCacheEntry.FetchedAt"/> and must be labelled as cache, never as live.
/// Failed lookups are not stored — the next search retries the network.
/// </summary>
public sealed class ProviderCache(
    MediTrailDbContext db,
    IOptions<DoctorRecommendationOptions> options) : IProviderCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly DoctorRecommendationOptions _options = options.Value;

    public static string BuildKey(string keyPrefix, ProviderQuery query)
    {
        var lat = Math.Round(query.Latitude, 4, MidpointRounding.AwayFromZero);
        var lng = Math.Round(query.Longitude, 4, MidpointRounding.AwayFromZero);
        var specialty = query.SpecialtyCode.Trim().ToLowerInvariant();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{keyPrefix}:{lat:0.0000},{lng:0.0000}:{query.RadiusMeters}:{specialty}");
    }

    public async Task<ProviderResult?> TryGetAsync(
        string keyPrefix, ProviderQuery query, CancellationToken ct = default)
    {
        var key = BuildKey(keyPrefix, query);
        var row = await db.ProviderCache.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CacheKey == key && c.ExpiresAt > DateTimeOffset.UtcNow, ct);

        if (row is null) return null;

        var payload = JsonSerializer.Deserialize<CachedPayload>(row.Payload.RootElement.GetRawText(), JsonOptions);
        if (payload is null) return null;

        var facilities = (payload.Facilities ?? [])
            .Select(f => f with
            {
                DistanceMeters = GeoMath.HaversineMeters(
                    query.Latitude, query.Longitude, f.Latitude, f.Longitude),
                Rating = null
            })
            .ToList();

        return new ProviderResult
        {
            Status = payload.Status,
            Facilities = facilities,
            FetchedAt = row.FetchedAt,
            EndpointUsed = payload.EndpointUsed,
            ServedFromCache = true
        };
    }

    public async Task SetAsync(
        string keyPrefix, ProviderQuery query, ProviderResult result, CancellationToken ct = default)
    {
        if (result.Status is not ProviderStatus.Ok and not ProviderStatus.Empty) return;

        var key = BuildKey(keyPrefix, query);
        var fetchedAt = result.FetchedAt ?? DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromHours(Math.Max(1, _options.CacheTtlHours));
        var payload = JsonDocument.Parse(JsonSerializer.Serialize(new CachedPayload
        {
            Status = result.Status,
            EndpointUsed = result.EndpointUsed,
            Facilities = result.Facilities.ToList()
        }, JsonOptions));

        var existing = await db.ProviderCache.FirstOrDefaultAsync(c => c.CacheKey == key, ct);
        if (existing is null)
        {
            db.ProviderCache.Add(new ProviderCacheEntry
            {
                CacheKey = key,
                Provider = keyPrefix,
                Payload = payload,
                FetchedAt = fetchedAt,
                ExpiresAt = fetchedAt.Add(ttl)
            });
        }
        else
        {
            existing.Provider = keyPrefix;
            existing.Payload = payload;
            existing.FetchedAt = fetchedAt;
            existing.ExpiresAt = fetchedAt.Add(ttl);
        }

        await db.SaveChangesAsync(ct);
    }

    private sealed record CachedPayload
    {
        public ProviderStatus Status { get; init; }
        public string? EndpointUsed { get; init; }
        public List<NormalizedFacility>? Facilities { get; init; }
    }
}
