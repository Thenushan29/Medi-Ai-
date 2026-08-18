using System.Text.Json;

namespace MediTrail.Api.Data.Entities;

/// <summary>
/// Read-through HTTP cache for geocoders and search providers. Payload is the provider's
/// own JSON — never a fabricated facility. <see cref="FetchedAt"/> must be shown whenever
/// a cached payload is served.
/// </summary>
public class ProviderCacheEntry
{
    public required string CacheKey { get; set; }

    public required string Provider { get; set; }

    public required JsonDocument Payload { get; set; }

    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }
}
