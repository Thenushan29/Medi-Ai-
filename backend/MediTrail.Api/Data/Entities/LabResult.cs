namespace MediTrail.Api.Data.Entities;

/// <summary>One test value, from one document. Grouped by <see cref="TestNameStandard"/> to form a trend series.</summary>
public class LabResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PatientId { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    /// <summary>Test name as printed on the report.</summary>
    public string? TestName { get; set; }

    /// <summary>Standardized grouping key so the same test from different labs charts as one series (FR-4.3, FR-6.1).</summary>
    public string? TestNameStandard { get; set; }

    public decimal? ValueNumeric { get; set; }

    /// <summary>Non-numeric result — "Positive", "Trace". Charted as an annotation, not a point.</summary>
    public string? ValueText { get; set; }

    public string? Unit { get; set; }

    // Reference range as recorded on the document — we flag against the document's own range,
    // never against a range we invent (FR-6.3).
    public decimal? NormalMin { get; set; }
    public decimal? NormalMax { get; set; }
    public string? NormalRangeText { get; set; }

    /// <summary>Falls back to the document date when the report prints no separate test date.</summary>
    public DateOnly? TestDate { get; set; }

    /// <summary>Computed in code, never by the LLM (Principle 2: never use an LLM for arithmetic).</summary>
    public bool IsOutOfRange { get; set; }

    public string? SourceText { get; set; }
    public int? Confidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
