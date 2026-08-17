using MediTrail.Api.Contracts.Api;

namespace MediTrail.Api.Data.Entities;

/// <summary>
/// One completed question and its answer, kept so a reopened chat drawer is not blank.
///
/// §5.2 excluded chat persistence from Round 1 — "no demo or evaluation value; client-side state is
/// sufficient" — and that held until the drawer had multi-turn context worth losing. Closing it now
/// discards a conversation the follow-up handling depends on.
///
/// The whole answer is stored, not just its text: an answer that comes back without its citations,
/// its confidence or its consult flag is a weaker claim than the one that was originally shown, and
/// Principle 3 does not stop applying because a page was reloaded.
///
/// This is PHI. It cascades from the patient like every other child row (§12.4).
/// </summary>
public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }

    public required string Question { get; set; }

    public required string AnswerEn { get; set; }
    public string? AnswerTa { get; set; }
    public string? AnswerTanglish { get; set; }

    /// <summary>Which version was shown first, so a reloaded turn reads as it originally did.</summary>
    public AskedLanguage AskedLanguage { get; set; }

    public List<Guid> Citations { get; set; } = [];

    public int Confidence { get; set; }
    public bool SafetyRefusal { get; set; }
    public bool ConsultProfessional { get; set; }
    public bool FoundInDocuments { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
