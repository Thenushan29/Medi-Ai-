namespace MediTrail.Api.Contracts.Api;

public sealed record SpecialtyEvidenceDto
{
    public required string Type { get; init; }
    public required string Label { get; init; }
    public string? Source { get; init; }
    public string? SourceId { get; init; }
    public string? SourceUrl { get; init; }
}

public sealed record SpecialtyResolutionDto
{
    public required string Code { get; init; }
    public required string Label { get; init; }
    public required string ResolvedBy { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<SpecialtyEvidenceDto> Evidence { get; init; } = [];
}

public sealed record SearchOriginDto
{
    public string? ResolvedPlace { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required string Geocoder { get; init; }
}

public sealed record FacilityResultDto
{
    public required string SourceRef { get; init; }
    public string? Name { get; init; }
    public string? Category { get; init; }
    public string? SpecialtyTag { get; init; }
    public string? Address { get; init; }
    public required int DistanceMeters { get; init; }
    public string? Phone { get; init; }
    public string? Website { get; init; }
    public string? OpeningHours { get; init; }
    public required string AvailabilityMatch { get; init; }
    public required int RankScore { get; init; }
    public IReadOnlyList<string> RankReasons { get; init; } = [];
    public string? MapUrl { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
}

public sealed record DoctorSearchResponseDto
{
    public required Guid SearchId { get; init; }

    /// <summary>ok | empty | failed | location_not_found | not_configured</summary>
    public required string Status { get; init; }

    public SpecialtyResolutionDto? Specialty { get; init; }
    public SearchOriginDto? Origin { get; init; }
    public int? RadiusMeters { get; init; }
    public IReadOnlyList<int>? RadiusLadderUsed { get; init; }
    public required string Provider { get; init; }
    public required string ProviderStatus { get; init; }
    public required bool ServedFromCache { get; init; }
    public DateTimeOffset? FetchedAtUtc { get; init; }
    public string? Attribution { get; init; }
    public IReadOnlyList<FacilityResultDto> Results { get; init; } = [];
    public string? Message { get; init; }
    public int? SuggestedNextRadiusMeters { get; init; }
    public bool StaleCache { get; init; }
    public string? NearestLargerCity { get; init; }
    public IReadOnlyList<string>? SuggestedPlaces { get; init; }
}

public sealed record DoctorSearchSummaryDto
{
    public required Guid SearchId { get; init; }
    public required string SpecialtyCode { get; init; }
    public required string LocationText { get; init; }
    public string? ResolvedPlace { get; init; }
    public required string ProviderStatus { get; init; }
    public required int ResultCount { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }
    public required bool ServedFromCache { get; init; }
}

public sealed record SpecialtyOptionDto
{
    public required string Code { get; init; }
    public required string Label { get; init; }
}

public sealed record CreateDoctorSearchRequest
{
    public Guid? AlertId { get; init; }
    public string? SpecialtyOverride { get; init; }
    public string LocationText { get; init; } = string.Empty;
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string Availability { get; init; } = "anytime";
    public int? RadiusMeters { get; init; }
}

public sealed record ProviderHealthDto
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public int? LatencyMs { get; init; }
    public string? Endpoint { get; init; }
    public string? Detail { get; init; }
}
