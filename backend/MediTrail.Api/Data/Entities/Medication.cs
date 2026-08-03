namespace MediTrail.Api.Data.Entities;

/// <summary>
/// One prescribed drug, from one document. Rebuildable from <see cref="Document.RawExtractionJson"/>.
/// <see cref="DocumentId"/> is never optional — evidence linking (FR-8.4) depends on it.
/// </summary>
public class Medication
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PatientId { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    public string? BrandName { get; set; }

    /// <summary>Lowercase active ingredient — the join key for every cross-check (FR-4.2).</summary>
    public string? GenericName { get; set; }

    public decimal? StrengthValue { get; set; }
    public string? StrengthUnit { get; set; }
    public string? Dose { get; set; }

    /// <summary>Frequency as printed on the document.</summary>
    public string? Frequency { get; set; }

    /// <summary>Doses per day, normalized for numeric comparison (FR-4.4, FR-5.2).</summary>
    public decimal? FrequencyPerDay { get; set; }

    public string? Route { get; set; }
    public int? DurationDays { get; set; }
    public string? Instructions { get; set; }

    /// <summary>Prescription window, derived from document date + duration. Drives overlap detection (FR-5.1).</summary>
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>Exact printed text, shown beside the normalized value in the evidence viewer (FR-4.6).</summary>
    public string? SourceText { get; set; }

    public int? Confidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
