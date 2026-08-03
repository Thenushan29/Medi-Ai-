namespace MediTrail.Api.Data.Entities;

/// <summary>
/// A cross-check finding. Derived data — the whole table can be dropped and recomputed from the
/// normalized records (PRD §12.2).
/// </summary>
public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }

    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }

    public required string Title { get; set; }

    /// <summary>Generic names this finding is about. Drives the "which drugs" line in the UI.</summary>
    public List<string> InvolvedGenerics { get; set; } = [];

    // Bilingual by construction, not by post-hoc translation (Principle 6).
    public string? ExplanationEn { get; set; }
    public string? ExplanationTa { get; set; }

    /// <summary>What the user should actually do — never a dose or treatment change (§5.3).</summary>
    public string? SuggestedActionEn { get; set; }
    public string? SuggestedActionTa { get; set; }

    /// <summary>Composed score: model self-assessment + consistency checks + verification (§11.4).</summary>
    public int Confidence { get; set; }

    /// <summary>True on red severity or confidence &lt; 50 (§11.4 presentation mapping).</summary>
    public bool RequiresProfessionalConsult { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Pending;

    /// <summary>Short attributed excerpt from the FDA label. Never reproduced at length (§16.2).</summary>
    public string? VerificationExcerpt { get; set; }

    public string? VerificationSource { get; set; }

    /// <summary>
    /// Every document backing this finding. A same-document contradiction has one entry;
    /// a cross-visit interaction has several. Never empty (Principle 3).
    /// </summary>
    public List<Guid> EvidenceDocumentIds { get; set; } = [];

    /// <summary>Which pipeline stage raised it — "rules" or "llm". Deterministic findings are not AI-labelled.</summary>
    public string? DetectedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
