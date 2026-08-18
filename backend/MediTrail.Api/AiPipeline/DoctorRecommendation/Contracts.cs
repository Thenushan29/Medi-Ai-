namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

/// <summary>
/// Outcome of a single provider call. Empty is not Failed. NotConfigured is not Failed.
/// Missing facility fields stay null — never a placeholder name or a zero rating.
/// </summary>
public enum ProviderStatus
{
    Ok,
    Empty,
    Failed,
    NotConfigured
}

public enum GeocodeStatus
{
    Ok,
    LocationNotFound,
    Failed
}

public sealed record NormalizedFacility
{
    public required string Source { get; init; }
    public required string SourceRef { get; init; }
    public string? Name { get; init; }
    public string? Category { get; init; }
    public string? SpecialtyTag { get; init; }
    public string? Address { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public string? Phone { get; init; }
    public string? Website { get; init; }
    public string? OpeningHours { get; init; }

    /// <summary>OSM has no ratings. Only a Google provider may set this, and only when present.</summary>
    public double? Rating { get; init; }
}

public sealed record ProviderQuery
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required int RadiusMeters { get; init; }
    public required string SpecialtyCode { get; init; }
}

public sealed record ProviderResult
{
    public required ProviderStatus Status { get; init; }
    public IReadOnlyList<NormalizedFacility> Facilities { get; init; } = [];
    public DateTimeOffset? FetchedAt { get; init; }
    public string? EndpointUsed { get; init; }
}

public interface IDoctorSearchProvider
{
    string Source { get; }

    Task<ProviderResult> SearchAsync(ProviderQuery query, CancellationToken ct = default);
}

public sealed record GeocodeResult
{
    public required GeocodeStatus Status { get; init; }
    public string? ResolvedPlace { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? Geocoder { get; init; }
    public bool ServedFromCache { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }
}

public interface IGeocoder
{
    Task<GeocodeResult> GeocodeAsync(string locationText, CancellationToken ct = default);
}

public sealed record DoctorSearchRequest
{
    public Guid? AlertId { get; init; }
    public string? SpecialtyOverride { get; init; }
    public required string LocationText { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string Availability { get; init; } = "anytime";
    public int? RadiusMeters { get; init; }
}

public interface IDoctorRecommendationService
{
    Task<Contracts.Api.DoctorSearchResponseDto> SearchAsync(
        Guid patientId, DoctorSearchRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<Contracts.Api.DoctorSearchSummaryDto>> ListAsync(
        Guid patientId, CancellationToken ct = default);

    IReadOnlyList<Contracts.Api.SpecialtyOptionDto> Specialties();
}
