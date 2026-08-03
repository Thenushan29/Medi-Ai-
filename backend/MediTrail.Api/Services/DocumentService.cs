using System.Security.Cryptography;
using System.Text.Json;
using MediTrail.Api.AiPipeline;
using MediTrail.Api.Configuration;
using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.Services;

public interface IDocumentService
{
    Task<UploadResultDto> UploadAsync(Guid patientId, IReadOnlyList<IFormFile> files, string? visitLabel, CancellationToken ct = default);
    Task<ProcessingStatusDto?> GetStatusAsync(Guid patientId, CancellationToken ct = default);
    Task<IReadOnlyList<TimelineEntryDto>> GetTimelineAsync(Guid patientId, CancellationToken ct = default);
    Task<DocumentDetailDto?> GetDocumentAsync(Guid documentId, CancellationToken ct = default);
}

public sealed class DocumentService(
    MediTrailDbContext db,
    IStorageService storage,
    IProcessingQueue queue,
    IOptions<PipelineOptions> pipelineOptions,
    ILogger<DocumentService> logger) : IDocumentService
{
    private readonly PipelineOptions _options = pipelineOptions.Value;

    /// <summary>
    /// Writes each accepted file to storage, creates its row, and queues it. Returns as soon as the
    /// rows are committed — processing continues in the background (FR-2.8).
    /// A rejected file never blocks the rest of the batch; both lists come back to the caller.
    /// </summary>
    public async Task<UploadResultDto> UploadAsync(
        Guid patientId, IReadOnlyList<IFormFile> files, string? visitLabel, CancellationToken ct = default)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, ct)
            ?? throw new NotFoundException($"Patient {patientId} was not found.");

        var accepted = new List<UploadedFileDto>();
        var rejected = new List<RejectedFileDto>();
        var queued = new List<ProcessingJob>();

        foreach (var file in files)
        {
            var rejection = Validate(file);
            if (rejection is not null)
            {
                rejected.Add(new RejectedFileDto { FileName = file.FileName, Reason = rejection });
                continue;
            }

            try
            {
                var (hash, bytes) = await HashAndReadAsync(file, ct);
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

                var document = new Document
                {
                    PatientId = patientId,
                    OriginalFileName = file.FileName,
                    ContentType = file.ContentType,
                    SizeBytes = file.Length,
                    Sha256 = hash,
                    VisitLabel = visitLabel,
                    StoragePath = string.Empty
                };
                document.StoragePath = $"{patientId}/{document.Id}{extension}";

                using (var stream = new MemoryStream(bytes))
                {
                    await storage.UploadAsync(document.StoragePath, stream, file.ContentType, ct);
                }

                // Extraction caching (FR-2.6): an identical file already read anywhere in the system
                // is copied rather than re-billed. The file is still stored — originals are never shared.
                var cached = _options.EnableExtractionCache ? await FindCachedExtractionAsync(hash, ct) : null;

                if (cached is not null)
                {
                    ApplyCachedExtraction(document, cached);
                    logger.LogInformation("Reused cached extraction for {FileName} (hash {Hash})", file.FileName, hash[..12]);
                }
                else
                {
                    document.Status = DocumentStatus.Queued;
                    queued.Add(new ProcessingJob(patientId, document.Id));
                }

                db.Documents.Add(document);

                accepted.Add(new UploadedFileDto
                {
                    DocumentId = document.Id,
                    FileName = file.FileName,
                    Status = document.Status,
                    ReusedCachedExtraction = cached is not null
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Upload failed for {FileName}", file.FileName);
                rejected.Add(new RejectedFileDto
                {
                    FileName = file.FileName,
                    Reason = "Could not be stored. Please try uploading this file again."
                });
            }
        }

        if (accepted.Count > 0)
        {
            patient.Status = queued.Count > 0 ? PatientStatus.Extracting : PatientStatus.Ready;
            patient.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        // Enqueued only after the rows are committed, so the worker can never read a document
        // that does not exist yet.
        foreach (var job in queued)
        {
            await queue.EnqueueAsync(job, ct);
        }

        return new UploadResultDto { Accepted = accepted, Rejected = rejected };
    }

    public async Task<ProcessingStatusDto?> GetStatusAsync(Guid patientId, CancellationToken ct = default)
    {
        var patient = await db.Patients
            .Where(p => p.Id == patientId)
            .Select(p => new { p.Status, p.StatusMessage })
            .FirstOrDefaultAsync(ct);

        if (patient is null) return null;

        var documents = await db.Documents
            .Where(d => d.PatientId == patientId)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new DocumentStatusDto
            {
                DocumentId = d.Id,
                FileName = d.OriginalFileName,
                Status = d.Status,
                FailureReason = d.FailureReason,
                OverallConfidence = d.OverallConfidence
            })
            .ToListAsync(ct);

        var completed = documents.Count(d => d.Status is DocumentStatus.Extracted or DocumentStatus.Cached);
        var failed = documents.Count(d => d.Status is DocumentStatus.Failed);

        return new ProcessingStatusDto
        {
            PatientId = patientId,
            Status = patient.Status,
            StatusMessage = patient.StatusMessage,
            Total = documents.Count,
            Completed = completed,
            Failed = failed,
            IsComplete = patient.Status is PatientStatus.Ready or PatientStatus.Failed,
            Documents = documents
        };
    }

    /// <summary>
    /// Merged chronological view (FR-4.5). Undated documents sort last rather than disappearing —
    /// an unreadable date is not a reason to hide evidence.
    /// </summary>
    public async Task<IReadOnlyList<TimelineEntryDto>> GetTimelineAsync(Guid patientId, CancellationToken ct = default)
    {
        var rows = await db.Documents
            .Where(d => d.PatientId == patientId)
            .OrderBy(d => d.DocumentDate == null)
            .ThenBy(d => d.DocumentDate)
            .ThenBy(d => d.CreatedAt)
            .Select(d => new
            {
                d.Id,
                d.DocumentDate,
                d.VisitLabel,
                d.DocumentType,
                d.ProviderName,
                d.ProviderFacility,
                d.OriginalFileName,
                d.StoragePath,
                d.Status,
                d.FailureReason,
                d.OverallConfidence,
                d.LegibilityNotes,
                MedicationCount = d.Medications.Count,
                LabResultCount = d.LabResults.Count,
                OutOfRangeCount = d.LabResults.Count(l => l.IsOutOfRange),
                WarningCount = d.Allergies.Count(a => a.IsDocumentWarning)
            })
            .ToListAsync(ct);

        return rows.Select(r => new TimelineEntryDto
        {
            DocumentId = r.Id,
            DocumentDate = r.DocumentDate,
            VisitLabel = r.VisitLabel,
            DocumentType = r.DocumentType,
            ProviderName = r.ProviderName,
            ProviderFacility = r.ProviderFacility,
            FileName = r.OriginalFileName,
            SourceUrl = storage.GetUrl(r.StoragePath),
            Status = r.Status,
            FailureReason = r.FailureReason,
            OverallConfidence = r.OverallConfidence,
            LegibilityNotes = r.LegibilityNotes,
            MedicationCount = r.MedicationCount,
            LabResultCount = r.LabResultCount,
            OutOfRangeCount = r.OutOfRangeCount,
            WarningCount = r.WarningCount
        }).ToList();
    }

    public async Task<DocumentDetailDto?> GetDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await db.Documents
            .AsNoTracking()
            .Include(d => d.Medications)
            .Include(d => d.LabResults)
            .Include(d => d.Allergies)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null) return null;

        return new DocumentDetailDto
        {
            DocumentId = document.Id,
            PatientId = document.PatientId,
            FileName = document.OriginalFileName,
            ContentType = document.ContentType,
            SourceUrl = storage.GetUrl(document.StoragePath),
            Status = document.Status,
            FailureReason = document.FailureReason,
            DocumentDate = document.DocumentDate,
            DocumentType = document.DocumentType,
            ProviderName = document.ProviderName,
            OverallConfidence = document.OverallConfidence,
            LegibilityNotes = document.LegibilityNotes,
            ExtractionModel = document.ExtractionModel,
            Medications = document.Medications.Select(m => new MedicationDto
            {
                Id = m.Id,
                DocumentId = m.DocumentId,
                BrandName = m.BrandName,
                GenericName = m.GenericName,
                StrengthValue = m.StrengthValue,
                StrengthUnit = m.StrengthUnit,
                Frequency = m.Frequency,
                FrequencyPerDay = m.FrequencyPerDay,
                DurationDays = m.DurationDays,
                Instructions = m.Instructions,
                StartDate = m.StartDate,
                EndDate = m.EndDate,
                SourceText = m.SourceText,
                Confidence = m.Confidence
            }).ToList(),
            LabResults = document.LabResults.Select(l => new LabResultDto
            {
                Id = l.Id,
                DocumentId = l.DocumentId,
                TestName = l.TestName,
                TestNameStandard = l.TestNameStandard,
                ValueNumeric = l.ValueNumeric,
                ValueText = l.ValueText,
                Unit = l.Unit,
                NormalMin = l.NormalMin,
                NormalMax = l.NormalMax,
                NormalRangeText = l.NormalRangeText,
                TestDate = l.TestDate,
                IsOutOfRange = l.IsOutOfRange,
                SourceText = l.SourceText,
                Confidence = l.Confidence
            }).ToList(),
            Allergies = document.Allergies.Select(a => new AllergyDto
            {
                Id = a.Id,
                DocumentId = a.DocumentId,
                IsDocumentWarning = a.IsDocumentWarning,
                Substance = a.Substance,
                SubstanceGeneric = a.SubstanceGeneric,
                RelatesTo = a.RelatesTo,
                Reaction = a.Reaction,
                Severity = a.Severity,
                SourceText = a.SourceText,
                Confidence = a.Confidence
            }).ToList()
        };
    }

    /// <summary>Format and size gate (FR-2.2, FR-2.3). Returns null when the file is acceptable.</summary>
    private string? Validate(IFormFile file)
    {
        if (file.Length == 0)
            return "The file is empty.";

        if (file.Length > _options.MaxFileSizeBytes)
            return $"Larger than the {_options.MaxFileSizeBytes / (1024 * 1024)} MB limit.";

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !_options.AllowedExtensions.Contains(extension))
            return $"Unsupported file type. Accepted formats: {string.Join(", ", _options.AllowedExtensions)}.";

        // Extension and content type must agree — a .pdf sent as image/png is not something we guess about.
        if (!_options.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return $"Unsupported content type '{file.ContentType}'.";

        return null;
    }

    private static async Task<(string Hash, byte[] Bytes)> HashAndReadAsync(IFormFile file, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await using (var source = file.OpenReadStream())
        {
            await source.CopyToAsync(buffer, ct);
        }

        var bytes = buffer.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (hash, bytes);
    }

    private Task<Document?> FindCachedExtractionAsync(string hash, CancellationToken ct) =>
        db.Documents
            .AsNoTracking()
            .Where(d => d.Sha256 == hash && d.Status == DocumentStatus.Extracted && d.RawExtractionJson != null)
            .OrderByDescending(d => d.ExtractedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Copies a prior extraction onto a new document row. Only the derived fields are copied —
    /// storage path, id and upload metadata stay this document's own.
    /// </summary>
    private static void ApplyCachedExtraction(Document target, Document source)
    {
        target.RawExtractionJson = source.RawExtractionJson is null
            ? null
            : JsonDocument.Parse(source.RawExtractionJson.RootElement.GetRawText());
        target.ExtractionModel = source.ExtractionModel;
        target.DocumentType = source.DocumentType;
        target.DocumentDate = source.DocumentDate;
        target.ProviderName = source.ProviderName;
        target.ProviderFacility = source.ProviderFacility;
        target.OverallConfidence = source.OverallConfidence;
        target.LegibilityNotes = source.LegibilityNotes;
        target.Status = DocumentStatus.Cached;
        target.ExtractedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class NotFoundException(string message) : Exception(message);
