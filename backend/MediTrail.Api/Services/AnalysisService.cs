using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.Services;

/// <summary>Read side of the dashboard: alerts and the grouped medications table.</summary>
public interface IAnalysisService
{
    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(Guid patientId, CancellationToken ct = default);
    Task<IReadOnlyList<MedicationGroupDto>> GetMedicationsAsync(Guid patientId, CancellationToken ct = default);
}

public sealed class AnalysisService(
    MediTrailDbContext db,
    IStorageService storage) : IAnalysisService
{
    /// <summary>Severity-sorted, then by confidence — the most serious and best-supported first (§10.8).</summary>
    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(Guid patientId, CancellationToken ct = default)
    {
        var alerts = await db.Alerts
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .ToListAsync(ct);

        if (alerts.Count == 0) return [];

        var referenced = alerts.SelectMany(a => a.EvidenceDocumentIds).Distinct().ToList();

        var documents = await db.Documents
            .AsNoTracking()
            .Where(d => referenced.Contains(d.Id))
            .Select(d => new { d.Id, d.OriginalFileName, d.StoragePath, d.DocumentDate })
            .ToDictionaryAsync(d => d.Id, ct);

        return alerts
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.Confidence)
            .Select(a => new AlertDto
            {
                Id = a.Id,
                Type = a.Type,
                Severity = a.Severity,
                Title = a.Title,
                InvolvedGenerics = a.InvolvedGenerics,
                ExplanationEn = a.ExplanationEn,
                ExplanationTa = a.ExplanationTa,
                SuggestedActionEn = a.SuggestedActionEn,
                SuggestedActionTa = a.SuggestedActionTa,
                Confidence = a.Confidence,
                RequiresProfessionalConsult = a.RequiresProfessionalConsult,
                VerificationStatus = a.VerificationStatus,
                VerificationExcerpt = a.VerificationExcerpt,
                VerificationSource = a.VerificationSource,
                Evidence = a.EvidenceDocumentIds
                    .Where(documents.ContainsKey)
                    .Select(id => new EvidenceRefDto
                    {
                        DocumentId = id,
                        FileName = documents[id].OriginalFileName,
                        SourceUrl = storage.GetUrl(documents[id].StoragePath),
                        DocumentDate = documents[id].DocumentDate
                    })
                    .ToList(),
                DetectedBy = a.DetectedBy
            })
            .ToList();
    }

    /// <summary>
    /// Grouped by generic (§10.6). Rows whose generic could not be resolved are grouped by brand
    /// instead and still shown — the user must see everything that was on the page (US-2), even
    /// though those rows cannot take part in generic-keyed cross-checks.
    /// </summary>
    public async Task<IReadOnlyList<MedicationGroupDto>> GetMedicationsAsync(
        Guid patientId, CancellationToken ct = default)
    {
        var medications = await db.Medications
            .AsNoTracking()
            .Where(m => m.PatientId == patientId)
            .Include(m => m.Document)
            .ToListAsync(ct);

        if (medications.Count == 0) return [];

        var alerts = await db.Alerts
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .Select(a => new { a.Id, a.InvolvedGenerics, a.Severity })
            .ToListAsync(ct);

        return medications
            .GroupBy(m => m.GenericName ?? $"brand:{m.BrandName?.ToLowerInvariant()}")
            .Select(group =>
            {
                var first = group.First();
                var generic = first.GenericName;

                var related = generic is null
                    ? []
                    : alerts.Where(a => a.InvolvedGenerics.Contains(generic)).ToList();

                return new MedicationGroupDto
                {
                    GenericName = generic,
                    DisplayName = Display(generic ?? first.BrandName ?? "Unnamed medication"),
                    TherapeuticClass = DrugNameNormalizer.ClassOf(generic),
                    AlertIds = related.Select(a => a.Id).ToList(),
                    // Only red and amber highlight a row; an informational finding is not a conflict.
                    HasConflict = related.Any(a => a.Severity != AlertSeverity.Info),
                    FirstPrescribed = group.Min(m => m.StartDate),
                    LastPrescribed = group.Max(m => m.EndDate ?? m.StartDate),
                    Rows = group
                        .OrderBy(m => m.StartDate ?? DateOnly.MaxValue)
                        .Select(m => new MedicationRowDto
                        {
                            Id = m.Id,
                            DocumentId = m.DocumentId,
                            SourceUrl = m.Document is null ? string.Empty : storage.GetUrl(m.Document.StoragePath),
                            BrandName = m.BrandName,
                            StrengthValue = m.StrengthValue,
                            StrengthUnit = m.StrengthUnit,
                            Frequency = m.Frequency,
                            FrequencyPerDay = m.FrequencyPerDay,
                            DurationDays = m.DurationDays,
                            Instructions = m.Instructions,
                            ProviderName = m.Document?.ProviderName,
                            StartDate = m.StartDate,
                            EndDate = m.EndDate,
                            SourceText = m.SourceText,
                            Confidence = m.Confidence
                        })
                        .ToList()
                };
            })
            // Conflicts first — the reason someone opened this screen.
            .OrderByDescending(g => g.HasConflict)
            .ThenBy(g => g.DisplayName)
            .ToList();
    }

    private static string Display(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
