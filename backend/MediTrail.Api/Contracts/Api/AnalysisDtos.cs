using System.ComponentModel.DataAnnotations;
using MediTrail.Api.Data.Entities;

namespace MediTrail.Api.Contracts.Api;

/// <summary>One card on the Alerts view (§10.8).</summary>
public sealed record AlertDto
{
    public required Guid Id { get; init; }
    public required AlertType Type { get; init; }
    public required AlertSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<string> InvolvedGenerics { get; init; }

    public string? ExplanationEn { get; init; }
    public string? ExplanationTa { get; init; }
    public string? SuggestedActionEn { get; init; }
    public string? SuggestedActionTa { get; init; }

    public required int Confidence { get; init; }
    public required bool RequiresProfessionalConsult { get; init; }

    public required VerificationStatus VerificationStatus { get; init; }
    public string? VerificationExcerpt { get; init; }
    public string? VerificationSource { get; init; }

    /// <summary>Every document behind this finding. Never empty (Principle 3).</summary>
    public required IReadOnlyList<EvidenceRefDto> Evidence { get; init; }

    /// <summary>"rules" or "llm" — a computed finding is not labelled as AI-generated (§17.3).</summary>
    public string? DetectedBy { get; init; }
}

public sealed record EvidenceRefDto
{
    public required Guid DocumentId { get; init; }
    public required string FileName { get; init; }
    public required string SourceUrl { get; init; }
    public DateOnly? DocumentDate { get; init; }
}

/// <summary>Medications view (§10.6), grouped by generic with conflict markers.</summary>
public sealed record MedicationGroupDto
{
    /// <summary>Null when no generic could be resolved — the rows are still shown (US-2).</summary>
    public string? GenericName { get; init; }

    public required string DisplayName { get; init; }
    public string? TherapeuticClass { get; init; }
    public required IReadOnlyList<MedicationRowDto> Rows { get; init; }

    /// <summary>Alert ids touching this group, so the table can highlight the row inline.</summary>
    public required IReadOnlyList<Guid> AlertIds { get; init; }

    public required bool HasConflict { get; init; }
    public DateOnly? FirstPrescribed { get; init; }
    public DateOnly? LastPrescribed { get; init; }
}

public sealed record MedicationRowDto
{
    public required Guid Id { get; init; }
    public required Guid DocumentId { get; init; }
    public required string SourceUrl { get; init; }
    public string? BrandName { get; init; }
    public decimal? StrengthValue { get; init; }
    public string? StrengthUnit { get; init; }
    public string? Frequency { get; init; }
    public decimal? FrequencyPerDay { get; init; }
    public int? DurationDays { get; init; }
    public string? Instructions { get; init; }
    public string? ProviderName { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? SourceText { get; init; }
    public int? Confidence { get; init; }
}

/// <summary>Lab Trends view (§10.7): one chart per standardized test.</summary>
public sealed record LabTrendDto
{
    public required string TestKey { get; init; }
    public required string DisplayName { get; init; }
    public string? Unit { get; init; }
    public decimal? NormalMin { get; init; }
    public decimal? NormalMax { get; init; }
    public string? NormalRangeText { get; init; }

    /// <summary>Rising, Falling, Stable, or Insufficient when there are fewer than three points.</summary>
    public required string Direction { get; init; }

    public decimal? PercentChange { get; init; }
    public required int OutOfRangeCount { get; init; }
    public required bool LatestOutOfRange { get; init; }
    public required IReadOnlyList<LabTrendPointDto> Points { get; init; }

    public string? ExplanationEn { get; init; }
    public string? ExplanationTa { get; init; }
    public required int Confidence { get; init; }
}

public sealed record LabTrendPointDto
{
    public required DateOnly Date { get; init; }
    public required decimal Value { get; init; }
    public required bool IsOutOfRange { get; init; }
    public required Guid DocumentId { get; init; }
}

public sealed record AskRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(1000, MinimumLength = 2)]
    public string Question { get; init; } = string.Empty;

    /// <summary>
    /// Earlier turns in the open chat session, oldest first, so a follow-up like "when?" has
    /// something to resolve against (FR-7.2). Client-held — nothing is persisted, and a reopened
    /// drawer starts empty.
    ///
    /// Trimmed server-side regardless of what arrives: the client is not trusted to bound the
    /// prompt.
    /// </summary>
    public IReadOnlyList<ChatTurn> History { get; init; } = [];
}

/// <summary>One completed exchange. Context for the next question, never a source for it.</summary>
public sealed record ChatTurn
{
    [StringLength(1000)]
    public string Question { get; init; } = string.Empty;

    [StringLength(4000)]
    public string Answer { get; init; } = string.Empty;
}

/// <summary>Chat answer with citations, confidence and the consult flag (FR-7.3, 7.4, 7.6).</summary>
public sealed record ChatAnswerDto
{
    public required string AnswerEn { get; init; }
    public string? AnswerTa { get; init; }
    public required IReadOnlyList<Guid> Citations { get; init; }
    public required int Confidence { get; init; }
    public required bool ConsultProfessional { get; init; }
    public required bool FoundInDocuments { get; init; }
}
