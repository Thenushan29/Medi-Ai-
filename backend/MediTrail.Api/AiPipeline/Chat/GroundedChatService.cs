using System.Text;
using System.Text.Json.Serialization;
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

public sealed class GroundedChatService(
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

            var completion = await ai.CompleteAsync(prompt, question.Trim(), ct: ct);

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
            .Include(d => d.Allergies)
            .OrderBy(d => d.DocumentDate == null)
            .ThenBy(d => d.DocumentDate)
            .ToListAsync(ct);

        if (documents.Count == 0) return string.Empty;

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
                if (m.Frequency is not null) builder.Append($", {m.Frequency}");
                if (m.DurationDays is not null) builder.Append($", {m.DurationDays} days");
                builder.AppendLine();
            }

            foreach (var l in document.LabResults)
            {
                builder.Append("- Lab: ").Append(l.TestName ?? l.TestNameStandard);
                builder.Append($" = {l.ValueNumeric?.ToString() ?? l.ValueText} {l.Unit}");
                if (l.NormalRangeText is not null) builder.Append($" (normal {l.NormalRangeText})");
                if (l.IsOutOfRange) builder.Append(" [OUTSIDE RANGE]");
                builder.AppendLine();
            }

            foreach (var a in document.Allergies)
            {
                builder.AppendLine(a.IsDocumentWarning
                    ? $"- Warning printed on this document: \"{a.Substance}\" (concerns: {string.Join(", ", a.RelatesTo)})"
                    : $"- Allergy: {a.Substance}{(a.Reaction is null ? "" : $" — {a.Reaction}")}");
            }

            builder.AppendLine();
        }

        if (alerts.Count > 0)
        {
            builder.AppendLine("## Findings already raised by the system");
            foreach (var alert in alerts)
            {
                builder.AppendLine($"- [{alert.Severity}] {alert.Title}: {alert.ExplanationEn}");
            }
        }

        return builder.ToString();
    }

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
