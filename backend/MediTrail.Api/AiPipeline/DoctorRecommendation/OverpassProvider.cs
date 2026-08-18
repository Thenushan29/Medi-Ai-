using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediTrail.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

/// <summary>
/// OpenStreetMap Overpass search. POST only (GET is flaky). Three-endpoint failover.
/// A 200 with zero usable elements is Empty, not Failed. Transport/5xx/HTML on every
/// endpoint is Failed. Missing tags stay null — OSM has no ratings.
/// </summary>
public sealed class OverpassProvider(
    HttpClient http,
    IOptions<DoctorRecommendationOptions> options,
    ILogger<OverpassProvider> logger) : IDoctorSearchProvider
{
    public const string HttpClientName = "Overpass";
    public const string Attribution = "© OpenStreetMap contributors (ODbL)";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DoctorRecommendationOptions _options = options.Value;

    public string Source => "openstreetmap";

    public async Task<ProviderResult> SearchAsync(ProviderQuery query, CancellationToken ct = default)
    {
        var ql = BuildQuery(query.Latitude, query.Longitude, query.RadiusMeters, _options.OverpassTimeoutSeconds);
        Exception? lastError = null;

        foreach (var endpoint in _options.OverpassEndpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) continue;

            try
            {
                var payload = await PostAsync(endpoint, ql, ct);
                if (payload is null) continue;

                var facilities = Normalize(payload, query.Latitude, query.Longitude);
                var fetchedAt = DateTimeOffset.UtcNow;

                if (facilities.Count == 0)
                {
                    logger.LogInformation("Overpass returned no usable facilities from {Host}", Host(endpoint));
                    return new ProviderResult
                    {
                        Status = ProviderStatus.Empty,
                        Facilities = [],
                        FetchedAt = fetchedAt,
                        EndpointUsed = endpoint
                    };
                }

                logger.LogInformation("Overpass returned {Count} facilities from {Host}", facilities.Count, Host(endpoint));
                return new ProviderResult
                {
                    Status = ProviderStatus.Ok,
                    Facilities = facilities,
                    FetchedAt = fetchedAt,
                    EndpointUsed = endpoint
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "Overpass endpoint {Host} failed; trying next", Host(endpoint));
            }
        }

        logger.LogWarning(lastError, "Every Overpass endpoint failed");
        return new ProviderResult { Status = ProviderStatus.Failed, Facilities = [] };
    }

    public static string BuildQuery(double lat, double lng, int radiusMeters, int timeoutSeconds)
    {
        var latS = lat.ToString("0.######", CultureInfo.InvariantCulture);
        var lngS = lng.ToString("0.######", CultureInfo.InvariantCulture);
        return
            $"""
            [out:json][timeout:{timeoutSeconds}];
            (
              nwr(around:{radiusMeters},{latS},{lngS})["amenity"~"^(doctors|clinic|hospital|pharmacy)$"];
              nwr(around:{radiusMeters},{latS},{lngS})["healthcare"];
            );
            out center tags;
            """;
    }

    private async Task<OverpassResponse?> PostAsync(string endpoint, string query, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Overpass {Host} returned {Status}", Host(endpoint), (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var media = response.Content.Headers.ContentType?.MediaType;
        if (LooksLikeHtml(media, body))
        {
            logger.LogWarning("Overpass {Host} returned HTML instead of JSON", Host(endpoint));
            return null;
        }

        var parsed = JsonSerializer.Deserialize<OverpassResponse>(body, JsonOptions);
        if (parsed is null)
        {
            logger.LogWarning("Overpass {Host} returned unreadable JSON", Host(endpoint));
            return null;
        }

        return parsed;
    }

    private static List<NormalizedFacility> Normalize(OverpassResponse payload, double originLat, double originLng)
    {
        var results = new List<NormalizedFacility>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in payload.Elements ?? [])
        {
            var facility = MapElement(element, originLat, originLng);
            if (facility is null) continue;
            if (!seen.Add(facility.SourceRef)) continue;
            results.Add(facility);
        }

        return results;
    }

    private static NormalizedFacility? MapElement(OverpassElement element, double originLat, double originLng)
    {
        if (string.IsNullOrWhiteSpace(element.Type) || element.Id is null) return null;

        var lat = element.Lat ?? element.Center?.Lat;
        var lng = element.Lon ?? element.Center?.Lon;
        if (lat is null || lng is null) return null;

        var tags = element.Tags ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (!IsHealthcare(tags)) return null;

        return new NormalizedFacility
        {
            Source = "openstreetmap",
            SourceRef = $"{element.Type}/{element.Id.Value}",
            Name = ReadTag(tags, "name"),
            Category = ReadTag(tags, "amenity") ?? ReadTag(tags, "healthcare"),
            SpecialtyTag = ReadTag(tags, "healthcare:speciality") ?? ReadTag(tags, "healthcare:specialty"),
            Address = ComposeAddress(tags),
            Latitude = lat.Value,
            Longitude = lng.Value,
            DistanceMeters = GeoMath.HaversineMeters(originLat, originLng, lat.Value, lng.Value),
            Phone = ReadTag(tags, "phone") ?? ReadTag(tags, "contact:phone"),
            Website = ReadTag(tags, "website") ?? ReadTag(tags, "contact:website"),
            OpeningHours = ReadTag(tags, "opening_hours"),
            Rating = null
        };
    }

    private static bool IsHealthcare(IReadOnlyDictionary<string, string> tags) =>
        ReadTag(tags, "amenity") is not null || ReadTag(tags, "healthcare") is not null;

    private static string? ComposeAddress(IReadOnlyDictionary<string, string> tags)
    {
        var full = ReadTag(tags, "addr:full");
        if (full is not null) return full;

        var parts = new[]
        {
            JoinHouse(ReadTag(tags, "addr:housenumber"), ReadTag(tags, "addr:street")),
            ReadTag(tags, "addr:city") ?? ReadTag(tags, "addr:district"),
            ReadTag(tags, "addr:postcode")
        }.Where(p => p is not null).Cast<string>().ToArray();

        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    private static string? JoinHouse(string? number, string? street)
    {
        if (number is null) return street;
        if (street is null) return number;
        return $"{number} {street}";
    }

    private static string? ReadTag(IReadOnlyDictionary<string, string> tags, string key)
    {
        if (!tags.TryGetValue(key, out var value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool LooksLikeHtml(string? mediaType, string body) =>
        (mediaType is not null && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        || body.StartsWith("<", StringComparison.Ordinal);

    private static string Host(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : endpoint;

    private sealed record OverpassResponse
    {
        [JsonPropertyName("elements")] public List<OverpassElement>? Elements { get; init; }
    }

    private sealed record OverpassElement
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("id")] public long? Id { get; init; }
        [JsonPropertyName("lat")] public double? Lat { get; init; }
        [JsonPropertyName("lon")] public double? Lon { get; init; }
        [JsonPropertyName("center")] public OverpassCenter? Center { get; init; }
        [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; init; }
    }

    private sealed record OverpassCenter
    {
        [JsonPropertyName("lat")] public double Lat { get; init; }
        [JsonPropertyName("lon")] public double Lon { get; init; }
    }
}
