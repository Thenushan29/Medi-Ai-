using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediTrail.Api.AiPipeline.Extraction;
using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.AiPipeline.Chat;

/// <summary>
/// Stage 7 (§11.1): questions answered strictly from the patient's own record (FR-7.1).
///
/// No vector store and no retrieval — the per-patient corpus is ten to fifteen documents, and
/// grounding on the *complete* structured record is both simpler and more accurate than top-k
/// retrieval at this scale (§5.2). Reasoning across all documents at once is also what makes
/// FR-7.2 possible at all.
/// </summary>
public interface IGroundedChatService
{
    Task<ChatAnswerDto> AskAsync(Guid patientId, string question, CancellationToken ct = default);
}

public sealed partial class GroundedChatService(
    MediTrailDbContext db,
    IPromptLibrary prompts,
    IServiceProvider services,
    ILogger<GroundedChatService> logger) : IGroundedChatService
{
    public async Task<ChatAnswerDto> AskAsync(Guid patientId, string question, CancellationToken ct = default)
    {
        var ai = services.GetService<IAiClient>();

        if (ai is null)
        {
            return Unavailable("The question service is not configured on this server.");
        }

        var record = await BuildRecordAsync(patientId, ct);

        if (record.Length == 0)
        {
            return new ChatAnswerDto
            {
                AnswerEn = "There is nothing in your records yet. Upload some documents and I can answer questions about them.",
                Citations = [],
                Confidence = 100,
                ConsultProfessional = false,
                FoundInDocuments = false
            };
        }

        try
        {
            var prompt = prompts.Get("chat", new Dictionary<string, string>
            {
                ["RECORD"] = record,
                ["QUESTION"] = question.Trim()
            });

            // The question is already inside the system prompt ({{QUESTION}}), surrounded by the
            // intent-matching instructions. Passing it again as the bare user message lets the
            // model answer the surface wording and skip those instructions — the pattern that
            // made "allergy noted in my earlier report" refuse a same-document finding sitting
            // in Findings. Sibling stages (cross-check, trends) pass a short fixed user turn for
            // the same reason.
            var completion = await ai.CompleteAsync(
                prompt,
                "Answer the question above as JSON. Prefer Findings over a not-found reply when they match the intent.",
                ct: ct);

            if (!JsonResponseReader.TryRead<ChatResponse>(completion.Content, out var answer, out var error))
            {
                logger.LogWarning("Chat answer unusable for {PatientId}: {Error}", patientId, error);
                return Unavailable("I could not put together an answer just now. Please try asking again.");
            }

            // Grounding check: a citation naming a document that is not this patient's is dropped
            // rather than shown. An evidence link that goes nowhere is worse than none (Principle 3).
            var known = await db.Documents
                .Where(d => d.PatientId == patientId)
                .Select(d => d.Id)
                .ToListAsync(ct);

            var citations = answer!.Citations
                .Select(c => Guid.TryParse(c, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty && known.Contains(id))
                .Distinct()
                .ToList();

            var confidence = Math.Clamp(answer.Confidence, 0, 100);

            return new ChatAnswerDto
            {
                AnswerEn = answer.AnswerEn ?? "I could not find that in your uploaded documents.",
                AnswerTa = answer.AnswerTa,
                Citations = citations,
                Confidence = confidence,
                // Forced on for low confidence regardless of what the model decided (§11.4, FR-7.6).
                ConsultProfessional = answer.ConsultProfessional || confidence < 50,
                FoundInDocuments = answer.FoundInDocuments
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Chat failed for {PatientId}", patientId);
            return Unavailable("I could not reach the question service. Please try again in a moment.");
        }
    }

    /// <summary>
    /// The complete structured record, with document ids so answers can cite them.
    /// Built from the normalized tables rather than raw extractions: the model should reason over
    /// what the pipeline actually concluded, which is also what the user sees on screen.
    /// </summary>
    private async Task<string> BuildRecordAsync(Guid patientId, CancellationToken ct)
    {
        var documents = await db.Documents
            .AsNoTracking()
            .Where(d => d.PatientId == patientId)
            .Include(d => d.Medications)
            .Include(d => d.LabResults)
            .OrderBy(d => d.DocumentDate == null)
            .ThenBy(d => d.DocumentDate)
            .ToListAsync(ct);

        if (documents.Count == 0) return string.Empty;

        // Scoped by patient, the way the rule checks read the same table — not through each
        // document's navigation. The two paths have to see the same rows, or chat can deny a
        // finding the dashboard is showing on the very same record.
        var allergies = await db.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .ToListAsync(ct);

        var allergiesByDocument = allergies
            .GroupBy(a => a.DocumentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var alerts = await db.Alerts
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.Severity)
            .ToListAsync(ct);

        var builder = new StringBuilder();

        foreach (var document in documents)
        {
            builder.AppendLine($"## Document id: {document.Id}");
            builder.AppendLine($"Date: {document.DocumentDate?.ToString("yyyy-MM-dd") ?? "not readable"}");
            builder.AppendLine($"Type: {document.DocumentType ?? "unknown"}");

            if (document.ProviderName is { } provider) builder.AppendLine($"Provider: {provider}");

            if (document.Status == DocumentStatus.Failed)
            {
                builder.AppendLine($"NOTE: this document could not be read ({document.FailureReason}).");
            }

            if (document.OverallConfidence is < 60)
            {
                // Told to the model so it can qualify an answer that rests on a weak reading.
                builder.AppendLine($"NOTE: this document read poorly (confidence {document.OverallConfidence}).");
            }

            foreach (var m in document.Medications)
            {
                builder.Append("- Medication: ").Append(m.GenericName ?? m.BrandName ?? "unnamed");
                if (m.BrandName is not null && m.GenericName is not null) builder.Append($" (brand {m.BrandName})");
                if (m.StrengthValue is not null) builder.Append($", {m.StrengthValue}{m.StrengthUnit}");
                if (m.Frequency is not null) builder.Append($", {OneLine(m.Frequency)}");
                if (m.DurationDays is not null) builder.Append($", {m.DurationDays} days");
                builder.AppendLine();
            }

            foreach (var l in document.LabResults)
            {
                builder.Append("- Lab: ").Append(OneLine(l.TestName ?? l.TestNameStandard));
                builder.Append($" = {l.ValueNumeric?.ToString() ?? OneLine(l.ValueText)} {l.Unit}");
                if (l.NormalRangeText is not null) builder.Append($" (normal {OneLine(l.NormalRangeText)})");
                if (l.IsOutOfRange) builder.Append(" [OUTSIDE RANGE]");
                builder.AppendLine();
            }

            foreach (var a in AllergiesFor(allergiesByDocument, document.Id))
            {
                builder.AppendLine(Describe(a));
            }

            builder.AppendLine();
        }

        // A row whose document is not in the list above would otherwise be silently absent from the
        // record while still driving a visible alert. There should be none — deleting a document
        // cascades — but chat denying something the dashboard shows is the failure worth guarding.
        var listed = documents.Select(d => d.Id).ToHashSet();
        var unlinked = allergies.Where(a => !listed.Contains(a.DocumentId)).ToList();

        if (unlinked.Count > 0)
        {
            builder.AppendLine("## Allergies and warnings whose document is not listed above");
            foreach (var a in unlinked) builder.AppendLine(Describe(a));
            builder.AppendLine();
        }

        if (alerts.Count > 0)
        {
            builder.AppendLine("## Findings already raised by the system");
            builder.AppendLine(
                "These are confirmed findings about this person. They answer questions about " +
                "allergies, earlier reports, prior notes, and drugs prescribed despite a warning — " +
                "including when the warning is on the SAME document as the medicine. " +
                "If a question's intent matches any finding below, answer from that finding and " +
                "cite the documents listed on it. Do not say not-found.");

            foreach (var alert in alerts)
            {
                builder.Append($"- [{alert.Severity}] {alert.Title}: {OneLine(alert.ExplanationEn)}");

                // Without the evidence ids an answer grounded on a finding cannot cite anything,
                // and an uncitable answer is dropped to "not found" by the grounding check above
                // (Principle 3).
                if (alert.EvidenceDocumentIds.Count > 0)
                {
                    builder.Append($" (documents: {string.Join(", ", alert.EvidenceDocumentIds)})");
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static List<Allergy> AllergiesFor(Dictionary<Guid, List<Allergy>> byDocument, Guid documentId) =>
        byDocument.TryGetValue(documentId, out var rows) ? rows : [];

    /// <summary>
    /// One row, one line. A printed warning is named as the contraindication it is and leads with
    /// the substances it concerns: the record is read by a model answering questions like "was I
    /// given anything I should avoid?", and a warning filed only as prose was being passed over
    /// (FR-5.5, FR-7.1).
    /// </summary>
    private static string Describe(Allergy allergy) => allergy.IsDocumentWarning
        ? $"- Warning printed on this document — do not take {string.Join(", ", allergy.RelatesTo)}: " +
          $"\"{OneLine(allergy.SourceText ?? allergy.Substance)}\""
        : $"- Allergy recorded for this person — do not take {allergy.Substance}" +
          (allergy.Reaction is null ? string.Empty : $" (reaction: {OneLine(allergy.Reaction)})");

    /// <summary>
    /// Collapses whitespace so one item stays on one line. Source text is transcribed from a
    /// document and carries the page's line breaks; left in, they split an entry across lines and
    /// the tail reads as a separate, malformed one.
    /// </summary>
    private static string? OneLine(string? value) =>
        value is null ? null : Whitespace().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static ChatAnswerDto Unavailable(string message) => new()
    {
        AnswerEn = message,
        Citations = [],
        Confidence = 0,
        ConsultProfessional = true,
        FoundInDocuments = false
    };

    private sealed record ChatResponse
    {
        [JsonPropertyName("answerEn")] public string? AnswerEn { get; init; }
        [JsonPropertyName("answerTa")] public string? AnswerTa { get; init; }
        [JsonPropertyName("citations")] public IReadOnlyList<string> Citations { get; init; } = [];
        [JsonPropertyName("confidence")] public int Confidence { get; init; }
        [JsonPropertyName("consultProfessional")] public bool ConsultProfessional { get; init; }
        [JsonPropertyName("foundInDocuments")] public bool FoundInDocuments { get; init; }
    }
}
