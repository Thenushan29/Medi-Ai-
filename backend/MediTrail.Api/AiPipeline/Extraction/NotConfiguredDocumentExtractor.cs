using MediTrail.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline.Extraction;

/// <summary>
/// Registered in place of <c>VisionDocumentExtractor</c> when no AI key is configured, so the
/// application still starts, uploads still persist, and the reason is visible per document.
///
/// It fails loudly rather than returning an empty extraction: an empty result would look like
/// "the document contained nothing", which is exactly the kind of confident-but-wrong output
/// Principle 1 forbids.
/// </summary>
public sealed class NotConfiguredDocumentExtractor(
    IOptions<AiOptions> options,
    ILogger<NotConfiguredDocumentExtractor> logger) : IDocumentExtractor
{
    public Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct = default)
    {
        var reason = string.IsNullOrWhiteSpace(options.Value.ApiKey)
            ? "AI extraction is not configured on this server (no Ai:ApiKey is set)."
            : "AI extraction is unavailable on this server.";

        logger.LogWarning("Extraction skipped for {DocumentId}: {Reason}", request.DocumentId, reason);
        return Task.FromResult(ExtractionResult.Failure(reason));
    }
}
