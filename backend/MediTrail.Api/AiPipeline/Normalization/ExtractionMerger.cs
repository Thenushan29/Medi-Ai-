using System.Text.Json;
using MediTrail.Api.Contracts.Extraction;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.AiPipeline.Normalization;

/// <summary>
/// Stage 2 of the pipeline (§11.1): raw extraction → normalized rows, merged into one
/// chronological record per patient (FR-4.5).
///
/// Entirely deterministic. Everything it writes is derived and rebuildable from
/// <c>documents.raw_extraction_json</c> (§12.2), which is what makes prompt tuning cheap: re-run
/// the merge, no re-upload.
/// </summary>
public interface IExtractionMerger
{
    /// <summary>Replaces this document's normalized rows from its stored extraction.</summary>
    Task MergeAsync(Guid documentId, CancellationToken ct = default);
}

public sealed class ExtractionMerger(
    MediTrailDbContext db,
    ILogger<ExtractionMerger> logger) : IExtractionMerger
{
    public async Task MergeAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await db.Documents
            .Include(d => d.Medications)
            .Include(d => d.LabResults)
            .Include(d => d.Allergies)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document?.RawExtractionJson is null)
        {
            logger.LogWarning("Nothing to merge for document {DocumentId}", documentId);
            return;
        }

        var extraction = document.RawExtractionJson.RootElement.Deserialize<DocumentExtraction>(JsonOptions);
        if (extraction is null)
        {
            logger.LogWarning("Stored extraction for {DocumentId} could not be deserialized", documentId);
            return;
        }

        // Idempotent: re-merging replaces rather than duplicates, so the pipeline can be re-run
        // after a prompt change without cleaning up first.
        db.Medications.RemoveRange(document.Medications);
        db.LabResults.RemoveRange(document.LabResults);
        db.Allergies.RemoveRange(document.Allergies);

        // Recomputed here rather than trusted from the model: a regex over the printed date cannot
        // hallucinate a year, and a wrong year silently reorders the whole timeline.
        document.DocumentDate = DateNormalizer.Parse(extraction.DocumentDate);

        // Added through the DbSets rather than the parent's navigation collections. These entities
        // generate their own Guid keys, and EF infers state from the key when it discovers an
        // entity by fixup — a non-default key reads as "already exists", so it issues an UPDATE
        // against a row that was never inserted. Adding explicitly states the intent.
        var medications = BuildMedications(document, extraction);
        var labResults = BuildLabResults(document, extraction);
        var allergies = BuildAllergiesAndWarnings(document, extraction);

        db.Medications.AddRange(medications);
        db.LabResults.AddRange(labResults);
        db.Allergies.AddRange(allergies);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Merged {DocumentId}: {Medications} medications, {Labs} lab results, {Allergies} allergies/warnings",
            documentId, medications.Count, labResults.Count, allergies.Count);
    }

    private static List<Medication> BuildMedications(Document document, DocumentExtraction extraction)
    {
        var medications = new List<Medication>();

        foreach (var source in extraction.Medications)
        {
            // The model resolves the generic where it can. When it could not, fall back to the
            // brand table: a row with no generic is excluded from every cross-check, so an
            // unresolved brand silently removes a medication from the safety analysis.
            var generic = DrugNameNormalizer.Normalize(source.GenericName)
                ?? DrugNameNormalizer.GenericForBrand(source.BrandName);

            // A brand with no resolved generic still deserves a row — the user must see everything
            // that was on the page (US-2). It simply cannot participate in generic-keyed checks.
            if (generic is null && DrugNameNormalizer.IsPlaceholder(source.BrandName))
            {
                // "DEMO MEDICINE 1" is not a drug. Recording it as one would put a fictional
                // medication in a patient's record (traps.md X6).
                continue;
            }

            // The model's own frequencyPerDay is a fallback: parsing the printed text is
            // reproducible, and the printed text is the evidence the user can check.
            var perDay = FrequencyNormalizer.PerDay(source.Frequency)
                ?? FrequencyNormalizer.PerDay(source.Instructions)
                ?? source.FrequencyPerDay;

            var start = document.DocumentDate;
            DateOnly? end = start is not null && source.DurationDays is > 0
                ? start.Value.AddDays(source.DurationDays.Value - 1)
                : null;

            // Models often echo the printed name into both fields when that name is itself the
            // generic, which shows the user "Brand: Paracetamol" — paracetamol is not a brand.
            // Cheaper and more reliable to drop the echo here than to keep asking the prompt not
            // to produce it.
            var brand = Trim(source.BrandName);
            if (generic is not null && DrugNameNormalizer.AreSameDrug(brand, generic))
            {
                brand = null;
            }

            medications.Add(new Medication
            {
                PatientId = document.PatientId,
                DocumentId = document.Id,
                BrandName = brand,
                GenericName = generic,
                StrengthValue = source.StrengthValue,
                StrengthUnit = Trim(source.StrengthUnit)?.ToLowerInvariant(),
                Dose = Trim(source.Dose),
                Frequency = Trim(source.Frequency),
                FrequencyPerDay = perDay,
                Route = Trim(source.Route)?.ToLowerInvariant(),
                DurationDays = source.DurationDays,
                Instructions = Trim(source.Instructions),
                StartDate = start,
                EndDate = end,
                SourceText = Trim(source.SourceText),
                Confidence = source.Confidence
            });
        }

        return medications;
    }

    private static List<LabResult> BuildLabResults(Document document, DocumentExtraction extraction)
    {
        var results = new List<LabResult>();

        foreach (var source in extraction.LabResults)
        {
            var standard = LabTestNormalizer.Standardize(source.TestNameStandard ?? source.TestName);

            results.Add(new LabResult
            {
                PatientId = document.PatientId,
                DocumentId = document.Id,
                TestName = Trim(source.TestName),
                TestNameStandard = standard,
                ValueNumeric = source.ValueNumeric,
                ValueText = Trim(source.ValueText),
                Unit = Trim(source.Unit),
                NormalMin = source.NormalMin,
                NormalMax = source.NormalMax,
                NormalRangeText = Trim(source.NormalRangeText),
                // Falls back to the document date when the report prints no separate test date.
                TestDate = DateNormalizer.Parse(source.TestDate) ?? document.DocumentDate,
                IsOutOfRange = LabTestNormalizer.IsOutOfRange(
                    source.ValueNumeric, source.NormalMin, source.NormalMax, source.NormalRangeText),
                SourceText = Trim(source.SourceText),
                Confidence = source.Confidence
            });
        }

        return results;
    }

    /// <summary>
    /// Allergies and printed warnings share a table, distinguished by a flag (§12.3). The warning
    /// case is what catches the same-document contradiction (FR-5.5), so its <c>relatesTo</c>
    /// generics are normalized through the same path as medication names — that is what makes
    /// "acetaminophen" collide with "Paracetamol".
    /// </summary>
    private static List<Allergy> BuildAllergiesAndWarnings(Document document, DocumentExtraction extraction)
    {
        var entries = new List<Allergy>();

        foreach (var source in extraction.Allergies)
        {
            var generic = DrugNameNormalizer.Normalize(source.SubstanceGeneric ?? source.Substance);

            entries.Add(new Allergy
            {
                PatientId = document.PatientId,
                DocumentId = document.Id,
                IsDocumentWarning = false,
                Substance = Trim(source.Substance),
                SubstanceGeneric = generic,
                RelatesTo = generic is null ? [] : [generic],
                Reaction = Trim(source.Reaction),
                Severity = Trim(source.Severity)?.ToLowerInvariant(),
                SourceText = Trim(source.SourceText),
                Confidence = source.Confidence
            });
        }

        foreach (var source in extraction.WarningsInDocument)
        {
            var generics = source.RelatesTo
                .Select(DrugNameNormalizer.Normalize)
                .Where(g => g is not null)
                .Select(g => g!)
                .Distinct()
                .ToList();

            // A warning naming no substance cannot be matched against anything, and keeping it
            // would dilute the list the contradiction check reads.
            if (generics.Count == 0) continue;

            entries.Add(new Allergy
            {
                PatientId = document.PatientId,
                DocumentId = document.Id,
                IsDocumentWarning = true,
                Substance = Trim(source.Text),
                SubstanceGeneric = generics.Count == 1 ? generics[0] : null,
                RelatesTo = generics,
                SourceText = Trim(source.SourceText),
                Confidence = source.Confidence
            });
        }

        return entries;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
