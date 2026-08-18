using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

public interface INominatimClient
{
    Task<NominatimLookup> SearchAsync(string query, CancellationToken ct = default);
}

public sealed record NominatimLookup
{
    public required GeocodeStatus Status { get; init; }
    public string? DisplayName { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

/// <summary>
/// Nominatim geocoder. 1 request/second, User-Agent required, Sri Lanka only.
/// Empty results are LocationNotFound. Transport errors are Failed — never thrown to the caller.
/// </summary>
public sealed class NominatimClient(
    HttpClient http,
    ILogger<NominatimClient> logger) : INominatimClient
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    public async Task<NominatimLookup> SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            await ThrottleAsync(ct);

            var url =
                $"search?q={Uri.EscapeDataString(query)}&format=json&limit=1&countrycodes=lk";

            using var response = await http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Nominatim returned {Status}", (int)response.StatusCode);
                return new NominatimLookup { Status = GeocodeStatus.Failed };
            }

            var hits = await response.Content.ReadFromJsonAsync<List<NominatimHit>>(ct);

            var hit = hits?.FirstOrDefault();
            if (hit is null
                || !double.TryParse(hit.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(hit.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
            {
                return new NominatimLookup { Status = GeocodeStatus.LocationNotFound };
            }

            return new NominatimLookup
            {
                Status = GeocodeStatus.Ok,
                DisplayName = hit.DisplayName,
                Latitude = lat,
                Longitude = lng
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Nominatim lookup failed");
            return new NominatimLookup { Status = GeocodeStatus.Failed };
        }
    }

    private static async Task ThrottleAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _nextAllowed)
            {
                await Task.Delay(_nextAllowed - now, ct);
            }

            _nextAllowed = DateTimeOffset.UtcNow.AddSeconds(1);
        }
        finally
        {
            Gate.Release();
        }
    }

    private sealed record NominatimHit
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
        [JsonPropertyName("lat")] public string? Lat { get; init; }
        [JsonPropertyName("lon")] public string? Lon { get; init; }
    }
}
