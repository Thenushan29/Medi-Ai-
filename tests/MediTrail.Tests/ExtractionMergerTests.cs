using System.Text.Json;
using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediTrail.Tests;

/// <summary>
/// Stage 2 (§11.1): what the merge writes into the normalized tables from a stored extraction.
/// Everything here is rebuildable from <c>documents.raw_extraction_json</c> (§12.2), so these
/// tests drive the merger from stored JSON exactly as a re-run would.
/// </summary>
public class ExtractionMergerTests : IDisposable
{
    private readonly MediTrailDbContext _db;
    private readonly ExtractionMerger _merger;
    private readonly Guid _patientId = Guid.NewGuid();

    public ExtractionMergerTests()
    {
        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseInMemoryDatabase($"merge-{Guid.NewGuid()}")
            .Options;

        _db = new MediTrailDbContext(options);
        _db.Patients.Add(new Patient { Id = _patientId, DisplayName = "Test" });
        _db.SaveChanges();

        _merger = new ExtractionMerger(_db, NullLogger<ExtractionMerger>.Instance);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// A warning row names the substance; the sentence is evidence and belongs in SourceText.
    /// Storing the sentence in both made the evidence viewer render a paragraph where a drug
    /// name goes.
    /// </summary>
    [Fact]
    public async Task WarningRowNamesTheSubstanceAndKeepsTheSentenceAsEvidence()
    {
        const string Sentence =
            "Avoid taking unnecessary or liver-toxic medications (e.g. alcohol, acetaminophen).";

        var documentId = AddDocument($$"""
        {
          "warningsInDocument": [
            { "text": "{{Sentence}}", "relatesTo": ["acetaminophen"], "confidence": 85 }
          ]
        }
        """);

        await _merger.MergeAsync(documentId);

        var warning = Assert.Single(await _db.Allergies.Where(a => a.IsDocumentWarning).ToListAsync());

        // The canonical generic, not the printed synonym: relatesTo goes through the same
        // normalization as medication names, which is what makes acetaminophen collide with
        // Paracetamol (FR-4.2).
        Assert.Equal("paracetamol", warning.Substance);
        Assert.Equal("paracetamol", warning.SubstanceGeneric);
        Assert.Equal(new[] { "paracetamol" }, warning.RelatesTo);

        // The full wording survives — the model printed no separate sourceText, so text is the
        // fallback rather than being lost (FR-4.6).
        Assert.Equal(Sentence, warning.SourceText);
    }

    /// <summary>Two substances in one warning: both named, and neither collapses into a single generic.</summary>
    [Fact]
    public async Task WarningNamingSeveralSubstancesListsThemAll()
    {
        var documentId = AddDocument("""
        {
          "warningsInDocument": [
            {
              "text": "Avoid NSAIDs and blood thinners before surgery.",
              "relatesTo": ["ibuprofen", "warfarin"],
              "sourceText": "ADVICE: Avoid NSAIDs and blood thinners before surgery.",
              "confidence": 80
            }
          ]
        }
        """);

        await _merger.MergeAsync(documentId);

        var warning = Assert.Single(await _db.Allergies.Where(a => a.IsDocumentWarning).ToListAsync());

        Assert.Equal("ibuprofen, warfarin", warning.Substance);
        Assert.Null(warning.SubstanceGeneric);
        // An explicit sourceText is preferred over the warning text when the model supplied one.
        Assert.Equal("ADVICE: Avoid NSAIDs and blood thinners before surgery.", warning.SourceText);
    }

    /// <summary>The patient-allergy branch is unaffected: substance stays as written.</summary>
    [Fact]
    public async Task AllergyRowKeepsTheSubstanceAsWritten()
    {
        var documentId = AddDocument("""
        {
          "allergies": [
            {
              "substance": "Penicillin",
              "reaction": "rash",
              "sourceText": "Known allergy: Penicillin (rash)",
              "confidence": 90
            }
          ]
        }
        """);

        await _merger.MergeAsync(documentId);

        var allergy = Assert.Single(await _db.Allergies.Where(a => !a.IsDocumentWarning).ToListAsync());

        Assert.Equal("Penicillin", allergy.Substance);
        Assert.Equal("penicillin", allergy.SubstanceGeneric);
        Assert.Equal("Known allergy: Penicillin (rash)", allergy.SourceText);
        Assert.Equal("rash", allergy.Reaction);
    }

    // ---- fixtures ----

    private Guid AddDocument(string rawExtractionJson)
    {
        var id = Guid.NewGuid();

        _db.Documents.Add(new Document
        {
            Id = id,
            PatientId = _patientId,
            OriginalFileName = $"{id}.png",
            ContentType = "image/png",
            StoragePath = $"{_patientId}/{id}.png",
            Sha256 = id.ToString("N") + id.ToString("N"),
            Status = DocumentStatus.Extracted,
            RawExtractionJson = JsonDocument.Parse(rawExtractionJson)
        });

        _db.SaveChanges();
        return id;
    }
}
