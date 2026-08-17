namespace MediTrail.Api.Data.Entities;

/// <summary>
/// A condition named on a document — "Diagnosis: MALARIA" printed above the prescription that
/// treats it. Rebuildable from <see cref="Document.RawExtractionJson"/> like every other normalized
/// row (§12.2). <see cref="DocumentId"/> is never optional — evidence linking depends on it.
///
/// **Transcription, never a conclusion.** This is what a doctor wrote on a page, stored exactly as
/// a medication is. Nothing in the pipeline reasons from it, maps it to a treatment, or presents it
/// as MediTrail's own finding; §5.3 and §17.1 forbid the product stating or implying a diagnosis,
/// and repeating a printed one back to the user is not that.
///
/// It exists because without it the record handed to the grounded chat model contains no condition
/// at all, and "what was I given for malaria?" is unanswerable on a document that prints the word
/// MALARIA directly above the four drugs (FR-7.2). The word has to be in the record for the model
/// to join the two.
/// </summary>
public class Diagnosis
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PatientId { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    /// <summary>The condition as named on the document. Not coded, not mapped to a terminology.</summary>
    public string? Text { get; set; }

    /// <summary>Exact printed text, shown beside the value in the evidence viewer (FR-4.6).</summary>
    public string? SourceText { get; set; }

    public int? Confidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
