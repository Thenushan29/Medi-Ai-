using MediTrail.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline.Extraction;

/// <summary>
/// Placeholder registered when no OpenRouter key is configured, and the seam the real
/// vision extractor (milestone M2) plugs into.
///
/// It fails loudly rather than returning an empty extraction: an empty result would look like
/// "the document contained nothing", which is exactly the kind of confident-but-wrong output
/// Principle 1 forbids.
/// </summary>
public sealed class NotConfiguredDocumentExtractor(
    IOptions<OpenRouterOptions> options,
    ILogger<NotConfiguredDocumentExtractor> logger) : IDocumentExtractor
{
    public Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct = default)
    {
        var reason = string.IsNullOrWhiteSpace(options.Value.ApiKey)
            ? "AI extraction is not configured on this server (missing OpenRouter API key)."
            : "AI extraction is not implemented yet (milestone M2).";

        logger.LogWarning("Extraction skipped for {DocumentId}: {Reason}", request.DocumentId, reason);
        return Task.FromResult(ExtractionResult.Failure(reason));
    }
}
