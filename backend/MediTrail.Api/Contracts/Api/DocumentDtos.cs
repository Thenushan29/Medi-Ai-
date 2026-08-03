using MediTrail.Api.Data.Entities;

namespace MediTrail.Api.Contracts.Api;

/// <summary>
/// Returned immediately after upload (FR-2.8) — before any extraction has run.
/// Per-file results so a partially rejected batch is still legible to the user.
/// </summary>
public sealed record UploadResultDto
{
    public required IReadOnlyList<UploadedFileDto> Accepted { get; init; }
    public required IReadOnlyList<RejectedFileDto> Rejected { get; init; }
}

public sealed record UploadedFileDto
{
    public required Guid DocumentId { get; init; }
    public required string FileName { get; init; }
    public required DocumentStatus Status { get; init; }

    /// <summary>True when an identical hash was already extracted and the result was reused (FR-2.6).</summary>
    public required bool ReusedCachedExtraction { get; init; }
}

/// <summary>A file refused before upload. The reason is shown to the user, never swallowed (FR-2.2).</summary>
public sealed record RejectedFileDto
{
    public required string FileName { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Drives the processing screen's stepper and per-document tick list (§10.3).</summary>
public sealed record ProcessingStatusDto
{
    public required Guid PatientId { get; init; }
    public required PatientStatus Status { get; init; }
    public string? StatusMessage { get; init; }
    public required int Total { get; init; }
    public required int Completed { get; init; }
    public required int Failed { get; init; }

    /// <summary>True once every document has reached a terminal state and analysis has finished.</summary>
    public required bool IsComplete { get; init; }

    public required IReadOnlyList<DocumentStatusDto> Documents { get; init; }
}

public sealed record DocumentStatusDto
{
    public required Guid DocumentId { get; init; }
    public required string FileName { get; init; }
    public required DocumentStatus Status { get; init; }
    public string? FailureReason { get; init; }
    public int? OverallConfidence { get; init; }
}

/// <summary>One card in the timeline (§10.5). Backed by v_patient_timeline.</summary>
public sealed record TimelineEntryDto
{
    public required Guid DocumentId { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public string? VisitLabel { get; init; }
    public string? DocumentType { get; init; }
    public string? ProviderName { get; init; }
    public string? ProviderFacility { get; init; }
    public required string FileName { get; init; }
    public required string SourceUrl { get; init; }
    public required DocumentStatus Status { get; init; }
    public string? FailureReason { get; init; }
    public int? OverallConfidence { get; init; }
    public string? LegibilityNotes { get; init; }
    public required int MedicationCount { get; init; }
    public required int LabResultCount { get; init; }
    public required int OutOfRangeCount { get; init; }
    public required int WarningCount { get; init; }
}

/// <summary>Evidence viewer payload (§10.9): the source image plus everything read from it.</summary>
public sealed record DocumentDetailDto
{
    public required Guid DocumentId { get; init; }
    public required Guid PatientId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required string SourceUrl { get; init; }
    public required DocumentStatus Status { get; init; }
    public string? FailureReason { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public string? DocumentType { get; init; }
    public string? ProviderName { get; init; }
    public int? OverallConfidence { get; init; }
    public string? LegibilityNotes { get; init; }
    public string? ExtractionModel { get; init; }
    public required IReadOnlyList<MedicationDto> Medications { get; init; }
    public required IReadOnlyList<LabResultDto> LabResults { get; init; }
    public required IReadOnlyList<AllergyDto> Allergies { get; init; }
}

public sealed record MedicationDto
{
    public required Guid Id { get; init; }
    public required Guid DocumentId { get; init; }
    public string? BrandName { get; init; }
    public string? GenericName { get; init; }
    public decimal? StrengthValue { get; init; }
    public string? StrengthUnit { get; init; }
    public string? Frequency { get; init; }
    public decimal? FrequencyPerDay { get; init; }
    public int? DurationDays { get; init; }
    public string? Instructions { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }

    /// <summary>Shown beside the normalized value so the user can check the reading themselves (US-7).</summary>
    public string? SourceText { get; init; }
    public int? Confidence { get; init; }
}

public sealed record LabResultDto
{
    public required Guid Id { get; init; }
    public required Guid DocumentId { get; init; }
    public string? TestName { get; init; }
    public string? TestNameStandard { get; init; }
    public decimal? ValueNumeric { get; init; }
    public string? ValueText { get; init; }
    public string? Unit { get; init; }
    public decimal? NormalMin { get; init; }
    public decimal? NormalMax { get; init; }
    public string? NormalRangeText { get; init; }
    public DateOnly? TestDate { get; init; }
    public required bool IsOutOfRange { get; init; }
    public string? SourceText { get; init; }
    public int? Confidence { get; init; }
}

public sealed record AllergyDto
{
    public required Guid Id { get; init; }
    public required Guid DocumentId { get; init; }
    public required bool IsDocumentWarning { get; init; }
    public string? Substance { get; init; }
    public string? SubstanceGeneric { get; init; }
    public required IReadOnlyList<string> RelatesTo { get; init; }
    public string? Reaction { get; init; }
    public string? Severity { get; init; }
    public string? SourceText { get; init; }
    public int? Confidence { get; init; }
}
