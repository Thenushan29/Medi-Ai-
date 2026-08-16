using System.ComponentModel.DataAnnotations;
using MediTrail.Api.Data.Entities;

namespace MediTrail.Api.Contracts.Api;

// DTOs are kept distinct from entities (§13 conventions) so the wire shape can change
// without dragging the persistence model with it.

public sealed record CreatePatientRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(200, MinimumLength = 1)]
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>Row on the patients list (§10.1): name, document count, last activity, risk chip.</summary>
public sealed record PatientSummaryDto
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public required PatientStatus Status { get; init; }
    public required int DocumentCount { get; init; }
    public required int RedAlertCount { get; init; }
    public required int AmberAlertCount { get; init; }
    /// <summary>
    /// Carried so the card can account for every finding. A patient whose only findings are
    /// informational otherwise shows no chip at all, which reads as "nothing was found".
    /// </summary>
    public required int InfoAlertCount { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? AnalyzedAt { get; init; }
}

/// <summary>Dashboard header (§10.4): counts, time span covered, summary chips.</summary>
public sealed record PatientDetailDto
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public required PatientStatus Status { get; init; }
    public string? StatusMessage { get; init; }
    public required int DocumentCount { get; init; }
    public required int FailedDocumentCount { get; init; }
    public required int RedAlertCount { get; init; }
    public required int AmberAlertCount { get; init; }
    public required int InfoAlertCount { get; init; }
    public DateOnly? EarliestDocumentDate { get; init; }
    public DateOnly? LatestDocumentDate { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? AnalyzedAt { get; init; }
}
