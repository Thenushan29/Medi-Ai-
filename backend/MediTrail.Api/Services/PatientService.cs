using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.Services;

public interface IPatientService
{
    Task<PatientDetailDto> CreateAsync(CreatePatientRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PatientSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<PatientDetailDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class PatientService(
    MediTrailDbContext db,
    IStorageService storage,
    ILogger<PatientService> logger) : IPatientService
{
    public async Task<PatientDetailDto> CreateAsync(CreatePatientRequest request, CancellationToken ct = default)
    {
        var patient = new Patient { DisplayName = request.DisplayName.Trim() };

        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created patient {PatientId}", patient.Id);

        return new PatientDetailDto
        {
            Id = patient.Id,
            DisplayName = patient.DisplayName,
            Status = patient.Status,
            DocumentCount = 0,
            FailedDocumentCount = 0,
            RedAlertCount = 0,
            AmberAlertCount = 0,
            InfoAlertCount = 0,
            CreatedAt = patient.CreatedAt,
            UpdatedAt = patient.UpdatedAt
        };
    }

    public async Task<IReadOnlyList<PatientSummaryDto>> ListAsync(CancellationToken ct = default) =>
        await db.Patients
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new PatientSummaryDto
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                Status = p.Status,
                DocumentCount = p.Documents.Count,
                RedAlertCount = p.Alerts.Count(a => a.Severity == AlertSeverity.Red),
                AmberAlertCount = p.Alerts.Count(a => a.Severity == AlertSeverity.Amber),
                InfoAlertCount = p.Alerts.Count(a => a.Severity == AlertSeverity.Info),
                UpdatedAt = p.UpdatedAt,
                AnalyzedAt = p.AnalyzedAt
            })
            .ToListAsync(ct);

    public async Task<PatientDetailDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Patients
            .Where(p => p.Id == id)
            .Select(p => new PatientDetailDto
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                Status = p.Status,
                StatusMessage = p.StatusMessage,
                DocumentCount = p.Documents.Count,
                FailedDocumentCount = p.Documents.Count(d => d.Status == DocumentStatus.Failed),
                RedAlertCount = p.Alerts.Count(a => a.Severity == AlertSeverity.Red),
                AmberAlertCount = p.Alerts.Count(a => a.Severity == AlertSeverity.Amber),
                InfoAlertCount = p.Alerts.Count(a => a.Severity == AlertSeverity.Info),
                // Time span covered, for the dashboard header chip (§10.4).
                EarliestDocumentDate = p.Documents.Min(d => d.DocumentDate),
                LatestDocumentDate = p.Documents.Max(d => d.DocumentDate),
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                AnalyzedAt = p.AnalyzedAt
            })
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Deletion cascades to every document, record and alert (§12.4). Stored files live outside the
    /// transaction, so they are removed first — an orphaned row is recoverable, an orphaned PHI file is not.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (patient is null) return false;

        await storage.DeletePrefixAsync(id.ToString(), ct);

        db.Patients.Remove(patient);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Deleted patient {PatientId} and all associated data", id);
        return true;
    }
}
