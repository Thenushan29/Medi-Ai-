using MediTrail.Api.Contracts.Extraction;

namespace MediTrail.Api.AiPipeline.Extraction;

/// <summary>Stage 1 of the pipeline (§11.1): document image → canonical schema JSON.</summary>
public interface IDocumentExtractor
{
    Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct = default);
}

public sealed record ExtractionRequest
{
    public required Guid DocumentId { get; init; }
    public required byte[] Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}

/// <summary>
/// Deliberately not an exception-based API: a failed extraction is an expected outcome that must be
/// recorded against the document and shown to the user, not thrown past the worker (§9.3).
/// </summary>
public sealed record ExtractionResult
{
    public required bool Succeeded { get; init; }

    /// <summary>Populated on success. Verbatim model output, already schema-validated.</summary>
    public DocumentExtraction? Extraction { get; init; }

    /// <summary>The raw JSON string as returned, stored as the immutable source of truth (§12.2).</summary>
    public string? RawJson { get; init; }

    /// <summary>Readable reason on failure — this reaches the user's screen (US-8).</summary>
    public string? FailureReason { get; init; }

    public string? Model { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public int? LatencyMs { get; init; }

    public static ExtractionResult Failure(string reason, string? model = null) =>
        new() { Succeeded = false, FailureReason = reason, Model = model };
}
