namespace MediTrail.Api.Data.Entities;

/// <summary>
/// A patient profile. Round 1 has no authentication (PRD §5.2) — the profile is the scoping
/// boundary for every other row in the database.
/// </summary>
public class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string DisplayName { get; set; }

    public PatientStatus Status { get; set; } = PatientStatus.Idle;

    /// <summary>Readable reason when <see cref="Status"/> is Failed. Surfaced, never swallowed.</summary>
    public string? StatusMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the patient-level analysis last completed successfully.</summary>
    public DateTimeOffset? AnalyzedAt { get; set; }

    public ICollection<Document> Documents { get; set; } = [];
    public ICollection<Alert> Alerts { get; set; } = [];
}
