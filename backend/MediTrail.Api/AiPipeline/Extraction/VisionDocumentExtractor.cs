using System.Diagnostics;
using System.Text.Json;
using MediTrail.Api.Contracts.Extraction;

namespace MediTrail.Api.AiPipeline.Extraction;

/// <summary>
/// Stage 1 of the pipeline (§11.1): document image → canonical schema JSON, via a vision model.
///
/// Failure policy per §11.5: malformed output gets exactly one retry with a stricter instruction,
/// then the document is marked failed. It is never salvaged into a partial extraction — a document
/// the model could not express cleanly is a document we do not trust.
/// </summary>
public sealed class VisionDocumentExtractor(
    IAiClient ai,
    IPromptLibrary prompts,
    IPdfRenderer pdfRenderer,
    ILogger<VisionDocumentExtractor> logger) : IDocumentExtractor
{
    public async Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        string? model = null;

        IReadOnlyList<byte[]> pages;
        string pageContentType;

        if (request.ContentType == "application/pdf")
        {
            try
            {
                // Rendered to images and sent through the same vision path as a photo (FR-2.7).
                // All pages go in one call: a two-page prescription is one prescribing event, and
                // extracting each page alone would separate the medication list from the advice
                // that qualifies it — which is exactly what the contradiction check needs together.
                pages = pdfRenderer.Render(request.Content);
                pageContentType = "image/png";

                logger.LogInformation("Rendered {Pages} page(s) from PDF {DocumentId}",
                    pages.Count, request.DocumentId);
            }
            catch (PdfRenderException ex)
            {
                logger.LogWarning(ex, "Could not render PDF {DocumentId}", request.DocumentId);
                return ExtractionResult.Failure(ex.Message);
            }
        }
        else
        {
            pages = [request.Content];
            pageContentType = request.ContentType;
        }

        try
        {
            var completion = await ai.CompleteWithImagesAsync(
                prompts.Get("extraction"), pages, pageContentType, ct: ct);

            model = completion.Model;

            if (JsonResponseReader.TryRead<DocumentExtraction>(completion.Content, out var extraction, out var error))
            {
                return Success(extraction!, completion, stopwatch);
            }

            logger.LogWarning("Malformed extraction JSON for {DocumentId}: {Error}. Retrying once.",
                request.DocumentId, error);

            // One stricter retry, showing the model the parser's own complaint.
            var retryPrompt = prompts.Get("extraction.retry",
                new Dictionary<string, string> { ["ERROR"] = error ?? "unknown" });

            var retry = await ai.CompleteWithImagesAsync(
                $"{prompts.Get("extraction")}\n\n---\n\n{retryPrompt}",
                pages, pageContentType, ct: ct);

            model = retry.Model;

            if (JsonResponseReader.TryRead<DocumentExtraction>(retry.Content, out var retried, out var retryError))
            {
                logger.LogInformation("Retry succeeded for {DocumentId}", request.DocumentId);
                return Success(retried!, retry, stopwatch, promptTokens: completion.PromptTokens + retry.PromptTokens,
                    completionTokens: completion.CompletionTokens + retry.CompletionTokens);
            }

            logger.LogError("Extraction failed for {DocumentId} after retry: {Error}",
                request.DocumentId, retryError);

            return ExtractionResult.Failure(
                "The AI could not read this document into a usable format. Try a clearer photo.", model);
        }
        catch (AiClientException ex)
        {
            logger.LogError(ex, "AI provider failure extracting {DocumentId}", request.DocumentId);
            return ExtractionResult.Failure(
                "The AI service could not be reached. This document was not read; try again shortly.", model);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected failure extracting {DocumentId}", request.DocumentId);
            return ExtractionResult.Failure($"Unexpected error while reading this document: {ex.Message}", model);
        }
    }

    private static ExtractionResult Success(
        DocumentExtraction extraction, AiCompletion completion, Stopwatch stopwatch,
        int? promptTokens = null, int? completionTokens = null)
    {
        stopwatch.Stop();

        return new ExtractionResult
        {
            Succeeded = true,
            Extraction = extraction,
            // Re-serialized from the parsed object so what is stored is exactly what was understood,
            // not the raw text with whatever wrapper came around it (§12.2 source of truth).
            RawJson = JsonSerializer.Serialize(extraction, SerializerOptions),
            Model = completion.Model,
            PromptTokens = promptTokens ?? completion.PromptTokens,
            CompletionTokens = completionTokens ?? completion.CompletionTokens,
            LatencyMs = (int)stopwatch.ElapsedMilliseconds
        };
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
