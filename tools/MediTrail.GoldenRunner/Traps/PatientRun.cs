using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.GoldenRunner.Traps;

/// <summary>
/// Everything one patient set produced, read back out of the database the pipeline wrote — not out
/// of the objects the checkers returned. The assertions have to see what a user would see.
/// </summary>
internal sealed record PatientRun(
    string Key,
    Guid PatientId,
    IReadOnlyList<DocumentRow> Documents,
    IReadOnlyList<MedicationRow> Medications,
    IReadOnlyList<AllergyRow> Allergies,
    IReadOnlyList<AlertRow> Alerts)
{
    public string NameOf(Guid documentId) =>
        Documents.FirstOrDefault(d => d.Id == documentId)?.Name ?? documentId.ToString();

    public DocumentRow? Document(string name) =>
        Documents.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static async Task<PatientRun> LoadAsync(
        MediTrailDbContext db, string key, Guid patientId, CancellationToken ct)
    {
        var documents = await db.Documents
            .AsNoTracking()
            .Where(d => d.PatientId == patientId)
            .OrderBy(d => d.OriginalFileName)
            .ToListAsync(ct);

        var names = documents.ToDictionary(
            d => d.Id, d => Path.GetFileNameWithoutExtension(d.OriginalFileName));

        var medications = await db.Medications.AsNoTracking()
            .Where(m => m.PatientId == patientId).ToListAsync(ct);
        var allergies = await db.Allergies.AsNoTracking()
            .Where(a => a.PatientId == patientId).ToListAsync(ct);
        var alerts = await db.Alerts.AsNoTracking()
            .Where(a => a.PatientId == patientId).ToListAsync(ct);

        string Name(Guid id) => names.TryGetValue(id, out var n) ? n : id.ToString();

        return new PatientRun(
            key,
            patientId,
            [.. documents.Select(d => new DocumentRow(
                d.Id,
                Name(d.Id),
                d.Sha256,
                d.Status,
                d.DocumentDate,
                RawDocumentDate(d),
                d.ExtractionModel,
                d.OverallConfidence,
                d.FailureReason,
                d.ExtractionLatencyMs,
                d.PromptTokens,
                d.CompletionTokens))],
            [.. medications.Select(m => new MedicationRow(
                Name(m.DocumentId), m.BrandName, m.GenericName,
                m.StrengthValue, m.StrengthUnit, m.SourceText))],
            [.. allergies.Select(a => new AllergyRow(
                Name(a.DocumentId), a.IsDocumentWarning, a.Substance,
                a.RelatesTo, a.SourceText))],
            [.. alerts
                .OrderByDescending(a => a.Severity)
                .ThenBy(a => a.Title)
                .Select(a => new AlertRow(
                    a.Type,
                    a.Severity,
                    a.Confidence,
                    a.Title,
                    a.InvolvedGenerics,
                    [.. a.EvidenceDocumentIds.Select(Name)],
                    a.RequiresProfessionalConsult,
                    a.VerificationStatus,
                    a.DetectedBy,
                    a.ExplanationEn))]);
    }

    /// <summary>
    /// The date string the model returned, before <c>DateNormalizer</c> saw it. A null
    /// <c>DocumentDate</c> can mean the model refused to guess or that it guessed something
    /// unparseable, and for a hallucination check those are not the same result.
    /// </summary>
    private static string? RawDocumentDate(Document document)
    {
        if (document.RawExtractionJson is null) return null;

        return document.RawExtractionJson.RootElement.TryGetProperty("documentDate", out var value)
            ? value.ToString()
            : null;
    }
}

internal sealed record DocumentRow(
    Guid Id,
    string Name,
    string Sha256,
    DocumentStatus Status,
    DateOnly? DocumentDate,
    string? RawDocumentDate,
    string? ExtractionModel,
    int? OverallConfidence,
    string? FailureReason,
    int? LatencyMs,
    int? PromptTokens,
    int? CompletionTokens);

internal sealed record MedicationRow(
    string Document,
    string? BrandName,
    string? GenericName,
    decimal? StrengthValue,
    string? StrengthUnit,
    string? SourceText);

internal sealed record AllergyRow(
    string Document,
    bool IsDocumentWarning,
    string? Substance,
    IReadOnlyList<string> RelatesTo,
    string? SourceText);

internal sealed record AlertRow(
    AlertType Type,
    AlertSeverity Severity,
    int Confidence,
    string Title,
    IReadOnlyList<string> InvolvedGenerics,
    IReadOnlyList<string> EvidenceDocuments,
    bool RequiresProfessionalConsult,
    VerificationStatus VerificationStatus,
    string? DetectedBy,
    string? ExplanationEn);
