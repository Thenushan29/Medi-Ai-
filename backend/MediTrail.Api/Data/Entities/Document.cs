using System.Text.Json;

namespace MediTrail.Api.Data.Entities;

/// <summary>
/// An uploaded source document. Together with <see cref="RawExtractionJson"/> this is the
/// source of truth (PRD §12.2): every normalized table below can be deleted and rebuilt from here,
/// which is what makes prompt tuning cheap — no re-upload required.
/// </summary>
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }

    public required string OriginalFileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Object-storage path: {patient_id}/{document_id}.{ext}. Immutable once written.</summary>
    public required string StoragePath { get; set; }

    /// <summary>SHA-256 of the file bytes. Exact re-upload reuses the cached extraction (FR-2.6).</summary>
    public required string Sha256 { get; set; }

    /// <summary>Optional user-supplied grouping label — "Year 1", a visit date (FR-2.4).</summary>
    public string? VisitLabel { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    /// <summary>Human-readable failure reason. Failure states must be informative, not silent (US-8).</summary>
    public string? FailureReason { get; set; }

    public int RetryCount { get; set; }

    /// <summary>The verbatim canonical-schema JSON returned by the vision model. Immutable except on re-processing.</summary>
    public JsonDocument? RawExtractionJson { get; set; }

    /// <summary>Which model produced the extraction, for traceability (FR-3.8).</summary>
    public string? ExtractionModel { get; set; }

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? ExtractionLatencyMs { get; set; }

    // ---- Denormalized from the extraction for cheap timeline reads ----
    public string? DocumentType { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderFacility { get; set; }

    /// <summary>0–100 model self-assessment for the document as a whole.</summary>
    public int? OverallConfidence { get; set; }
    public string? LegibilityNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExtractedAt { get; set; }

    public ICollection<Medication> Medications { get; set; } = [];
    public ICollection<Diagnosis> Diagnoses { get; set; } = [];
    public ICollection<LabResult> LabResults { get; set; } = [];
    public ICollection<Allergy> Allergies { get; set; } = [];
}
