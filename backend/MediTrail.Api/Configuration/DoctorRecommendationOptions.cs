namespace MediTrail.Api.Configuration;

/// <summary>Master switches. Doctor recommendation must default off so production stays Round 1.</summary>
public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool DoctorRecommendation { get; set; }
}

/// <summary>Endpoints, timeouts and cache for the doctor-recommendation feature (Round 2 §13).</summary>
public sealed class DoctorRecommendationOptions
{
    public const string SectionName = "DoctorRecommendation";

    /// <summary>overpass | google | healthsites | doc990</summary>
    public string Provider { get; set; } = "overpass";

    public string NominatimBaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    public string NominatimUserAgent { get; set; } = "MediTrail/1.0 (contact@example)";

    public int NominatimTimeoutSeconds { get; set; } = 10;

    public string[] OverpassEndpoints { get; set; } =
    [
        "https://overpass-api.de/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter"
    ];

    public int OverpassTimeoutSeconds { get; set; } = 25;

    public string RxClassBaseUrl { get; set; } = "https://rxnav.nlm.nih.gov/REST";

    public int RxClassTimeoutSeconds { get; set; } = 10;

    /// <summary>Search-result cache. Geocode city coords use a separate permanent TTL.</summary>
    public int CacheTtlHours { get; set; } = 24;

    public int DefaultRadiusMeters { get; set; } = 5000;

    public int FallbackRadiusMeters { get; set; } = 15000;
}
