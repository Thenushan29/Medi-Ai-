using System.Text.Json;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

/// <summary>
/// Nominatim first, static Sri Lankan city table as fallback, provider_cache with a permanent TTL.
/// Never throws for a miss.
/// </summary>
public sealed class Geocoder(
    MediTrailDbContext db,
    INominatimClient nominatim,
    ILogger<Geocoder> logger) : IGeocoder
{
    private static readonly TimeSpan PermanentTtl = TimeSpan.FromDays(365 * 50);

    public async Task<GeocodeResult> GeocodeAsync(string locationText, CancellationToken ct = default)
    {
        var query = locationText.Trim();
        if (query.Length == 0)
        {
            return new GeocodeResult { Status = GeocodeStatus.LocationNotFound };
        }

        var cacheKey = $"geocode:{query.ToLowerInvariant()}";
        var cached = await ReadCacheAsync(cacheKey, ct);
        if (cached is not null) return cached;

        var remote = await nominatim.SearchAsync(query, ct);

        if (remote.Status == GeocodeStatus.Ok && remote.Latitude is not null && remote.Longitude is not null)
        {
            var result = new GeocodeResult
            {
                Status = GeocodeStatus.Ok,
                ResolvedPlace = remote.DisplayName,
                Latitude = remote.Latitude,
                Longitude = remote.Longitude,
                Geocoder = "nominatim",
                ServedFromCache = false,
                FetchedAt = DateTimeOffset.UtcNow
            };
            await WriteCacheAsync(cacheKey, "nominatim", result, ct);
            return result;
        }

        if (StaticSriLankaCityTable.TryResolve(query, out var city, out var lat, out var lng))
        {
            var result = new GeocodeResult
            {
                Status = GeocodeStatus.Ok,
                ResolvedPlace = city,
                Latitude = lat,
                Longitude = lng,
                Geocoder = "static_city_table",
                ServedFromCache = false,
                FetchedAt = DateTimeOffset.UtcNow
            };
            await WriteCacheAsync(cacheKey, "static_city_table", result, ct);
            return result;
        }

        if (remote.Status == GeocodeStatus.Failed)
        {
            logger.LogWarning("Geocode failed for an unresolved place after Nominatim error");
            return new GeocodeResult { Status = GeocodeStatus.Failed };
        }

        return new GeocodeResult { Status = GeocodeStatus.LocationNotFound };
    }

    private async Task<GeocodeResult?> ReadCacheAsync(string cacheKey, CancellationToken ct)
    {
        var row = await db.ProviderCache.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CacheKey == cacheKey && c.ExpiresAt > DateTimeOffset.UtcNow, ct);

        if (row is null) return null;

        var root = row.Payload.RootElement;
        if (!root.TryGetProperty("lat", out var latEl) || !root.TryGetProperty("lng", out var lngEl))
        {
            return null;
        }

        return new GeocodeResult
        {
            Status = GeocodeStatus.Ok,
            ResolvedPlace = root.TryGetProperty("place", out var place) ? place.GetString() : null,
            Latitude = latEl.GetDouble(),
            Longitude = lngEl.GetDouble(),
            Geocoder = row.Provider,
            ServedFromCache = true,
            FetchedAt = row.FetchedAt
        };
    }

    private async Task WriteCacheAsync(string cacheKey, string provider, GeocodeResult result, CancellationToken ct)
    {
        var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            place = result.ResolvedPlace,
            lat = result.Latitude,
            lng = result.Longitude
        }));

        var existing = await db.ProviderCache.FirstOrDefaultAsync(c => c.CacheKey == cacheKey, ct);
        if (existing is null)
        {
            db.ProviderCache.Add(new ProviderCacheEntry
            {
                CacheKey = cacheKey,
                Provider = provider,
                Payload = payload,
                FetchedAt = result.FetchedAt ?? DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(PermanentTtl)
            });
        }
        else
        {
            existing.Provider = provider;
            existing.Payload = payload;
            existing.FetchedAt = result.FetchedAt ?? DateTimeOffset.UtcNow;
            existing.ExpiresAt = DateTimeOffset.UtcNow.Add(PermanentTtl);
        }

        await db.SaveChangesAsync(ct);
    }
}
