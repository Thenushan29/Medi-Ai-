namespace MediTrail.Api.Data.Entities;

/// <summary>
/// One facility from a live provider (or a labelled cache of one). Name, address, phone and
/// rating are nullable — missing is null, never a placeholder.
/// </summary>
public class DoctorSearchResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SearchId { get; set; }
    public DoctorSearch? Search { get; set; }

    public required string Source { get; set; }
    public required string SourceRef { get; set; }
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? SpecialtyTag { get; set; }
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int DistanceMeters { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? OpeningHours { get; set; }
    public required string AvailabilityMatch { get; set; }
    public int RankScore { get; set; }
    public List<string> RankReasons { get; set; } = [];
    public DateTimeOffset FetchedAt { get; set; }
}
