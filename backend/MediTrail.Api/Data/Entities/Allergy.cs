namespace MediTrail.Api.Data.Entities;

/// <summary>
/// Holds two related things, distinguished by <see cref="IsDocumentWarning"/> (PRD §12.3):
/// a recorded patient allergy, and a warning printed on a document ("avoid liver-toxic medications").
/// Both are matched against the full medication history — the warning case is what catches the
/// same-document contradiction (FR-5.5).
/// </summary>
public class Allergy
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PatientId { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    /// <summary>False = a recorded allergy. True = advice text printed on the document.</summary>
    public bool IsDocumentWarning { get; set; }

    /// <summary>
    /// The substance as written. For a warning this is the substance(s) it concerns, not the
    /// sentence — the sentence is evidence and lives in <see cref="SourceText"/>.
    /// </summary>
    public string? Substance { get; set; }

    /// <summary>Lowercase generic, so "Paracetamol" and "acetaminophen" collide correctly (US-4).</summary>
    public string? SubstanceGeneric { get; set; }

    /// <summary>
    /// Generic names a document warning refers to, when it names more than one.
    /// Empty for a plain allergy row, where <see cref="SubstanceGeneric"/> carries the single substance.
    /// </summary>
    public List<string> RelatesTo { get; set; } = [];

    public string? Reaction { get; set; }
    public string? Severity { get; set; }

    public string? SourceText { get; set; }
    public int? Confidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
