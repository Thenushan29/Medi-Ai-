using System.Text.Json;
using MediTrail.Api.AiPipeline.Extraction;
using MediTrail.Api.Configuration;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using MediTrail.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline;

/// <summary>
/// Hosted worker draining <see cref="IProcessingQueue"/>.
///
/// Two guarantees the PRD depends on:
///   • Per-document failure isolation (§14.4) — one bad file never aborts a batch.
///   • Restart recovery (§14.3) — <c>documents.status</c> is the durable record, so anything left
///     mid-flight when the process died is re-enqueued on startup.
/// </summary>
public sealed class ProcessingWorker(
    IProcessingQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<PipelineOptions> pipelineOptions,
    ILogger<ProcessingWorker> logger) : BackgroundService
{
    private readonly PipelineOptions _options = pipelineOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedWorkAsync(stoppingToken);

        using var concurrency = new SemaphoreSlim(_options.WorkerConcurrency);
        var inFlight = new List<Task>();

        await foreach (var job in queue.DequeueAllAsync(stoppingToken))
        {
            await concurrency.WaitAsync(stoppingToken);

            inFlight.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessDocumentAsync(job, stoppingToken);
                }
                finally
                {
                    concurrency.Release();
                }
            }, stoppingToken));

            inFlight.RemoveAll(t => t.IsCompleted);
        }

        await Task.WhenAll(inFlight);
    }

    /// <summary>
    /// Anything not in a terminal state when the process last stopped is picked back up.
    /// This is what lets an in-process channel stand in for a real broker (§14.2).
    /// </summary>
    private async Task RequeueUnfinishedWorkAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediTrailDbContext>();

            var unfinished = await db.Documents
                .Where(d => d.Status == DocumentStatus.Uploaded
                         || d.Status == DocumentStatus.Queued
                         || d.Status == DocumentStatus.Extracting)
                .Select(d => new ProcessingJob(d.PatientId, d.Id))
                .ToListAsync(ct);

            foreach (var job in unfinished)
            {
                await queue.EnqueueAsync(job, ct);
            }

            if (unfinished.Count > 0)
            {
                logger.LogInformation("Re-enqueued {Count} unfinished document(s) after startup", unfinished.Count);
            }
        }
        catch (Exception ex)
        {
            // A database that is not reachable yet must not crash the host; uploads will re-enqueue.
            logger.LogError(ex, "Could not re-enqueue unfinished work at startup");
        }
    }

    private async Task ProcessDocumentAsync(ProcessingJob job, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MediTrailDbContext>();

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == job.DocumentId, ct);
        if (document is null)
        {
            logger.LogWarning("Document {DocumentId} vanished before processing", job.DocumentId);
            return;
        }

        if (document.Status is DocumentStatus.Extracted or DocumentStatus.Cached)
        {
            return;
        }

        try
        {
            document.Status = DocumentStatus.Extracting;
            await db.SaveChangesAsync(ct);

            var storage = services.GetRequiredService<IStorageService>();
            var extractor = services.GetRequiredService<IDocumentExtractor>();

            var bytes = await storage.DownloadAsync(document.StoragePath, ct);

            var result = await extractor.ExtractAsync(new ExtractionRequest
            {
                DocumentId = document.Id,
                Content = bytes,
                ContentType = document.ContentType,
                FileName = document.OriginalFileName
            }, ct);

            document.ExtractionModel = result.Model;
            document.PromptTokens = result.PromptTokens;
            document.CompletionTokens = result.CompletionTokens;
            document.ExtractionLatencyMs = result.LatencyMs;

            if (!result.Succeeded)
            {
                document.Status = DocumentStatus.Failed;
                document.FailureReason = result.FailureReason;
                document.RetryCount++;
                logger.LogWarning("Extraction failed for {DocumentId}: {Reason}",
                    document.Id, result.FailureReason);
            }
            else
            {
                document.RawExtractionJson = result.RawJson is null ? null : JsonDocument.Parse(result.RawJson);
                document.Status = DocumentStatus.Extracted;
                document.ExtractedAt = DateTimeOffset.UtcNow;
                document.FailureReason = null;

                var extraction = result.Extraction;
                document.DocumentType = extraction?.DocumentType;
                document.ProviderName = extraction?.Provider?.Name;
                document.ProviderFacility = extraction?.Provider?.Facility;
                document.OverallConfidence = extraction?.OverallConfidence;
                document.LegibilityNotes = extraction?.LegibilityNotes;
                document.DocumentDate = ParseIsoDate(extraction?.DocumentDate);

                // Normalize & merge (stage 2) and everything downstream land here in M3.
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Isolation: this document fails, the rest of the batch continues (§14.4).
            logger.LogError(ex, "Unhandled error processing document {DocumentId}", document.Id);

            document.Status = DocumentStatus.Failed;
            document.FailureReason = $"Processing error: {ex.Message}";
            document.RetryCount++;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            await TryAdvancePatientAsync(services, db, job.PatientId, ct);
        }
    }

    /// <summary>
    /// Patient-level analysis runs once, after every document has reached a terminal state (§9.2).
    /// </summary>
    private async Task TryAdvancePatientAsync(
        IServiceProvider services, MediTrailDbContext db, Guid patientId, CancellationToken ct)
    {
        try
        {
            var pending = await db.Documents.CountAsync(d => d.PatientId == patientId
                && d.Status != DocumentStatus.Extracted
                && d.Status != DocumentStatus.Cached
                && d.Status != DocumentStatus.Failed, ct);

            if (pending > 0) return;

            // Guard against two documents finishing at once and both starting an analysis.
            if (!_analysisInFlight.TryAdd(patientId, 0)) return;

            try
            {
                var analyzer = services.GetRequiredService<IPatientAnalyzer>();
                await analyzer.AnalyzeAsync(patientId, ct);
            }
            finally
            {
                _analysisInFlight.TryRemove(patientId, out _);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not run analysis for patient {PatientId}", patientId);
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _analysisInFlight = new();

    private static DateOnly? ParseIsoDate(string? value) =>
        DateOnly.TryParse(value, out var parsed) ? parsed : null;
}
