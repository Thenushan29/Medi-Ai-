namespace MediTrail.Api.Data.Entities;

/// <summary>
/// One doctor-search attempt. Persisted so Empty / Failed / LocationNotFound are auditable
/// and a zero-result run cannot be confused with a successful one.
/// </summary>
public class DoctorSearch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }

    /// <summary>No FK: alerts are wiped on re-analysis. A dangling id is a missing alert, not a cascade.</summary>
    public Guid? AlertId { get; set; }

    public required string SpecialtyCode { get; set; }
    public required string SpecialtySource { get; set; }
    public required string LocationText { get; set; }
    public string? ResolvedPlace { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int RadiusMeters { get; set; }
    public required string Availability { get; set; }
    public required string Provider { get; set; }

    /// <summary>ok | empty | failed | not_configured | location_not_found</summary>
    public required string ProviderStatus { get; set; }

    public bool ServedFromCache { get; set; }
    public int ResultCount { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<DoctorSearchResult> Results { get; set; } = [];
    public ICollection<SpecialtyEvidence> Evidence { get; set; } = [];
}
