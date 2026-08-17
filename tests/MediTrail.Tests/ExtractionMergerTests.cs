using System.Runtime.CompilerServices;
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

    /// <summary>
    /// Diagnoses were extracted into the canonical schema and then dropped here, so the word
    /// "MALARIA" printed above four drugs never reached any downstream stage. Stored exactly as
    /// printed — no coding, no mapping to the drugs that usually treat it (§5.3).
    /// </summary>
    [Fact]
    public async Task PersistsDiagnosesAsPrinted()
    {
        var documentId = AddDocument("""
        {
          "diagnoses": [
            { "text": "Malaria", "sourceText": "* MALARIA", "confidence": 95 },
            { "text": "Fever with chills", "sourceText": "* FEVER WITH CHILLS" }
          ]
        }
        """);

        await _merger.MergeAsync(documentId);

        var diagnoses = await _db.Diagnoses.OrderBy(d => d.Text).ToListAsync();

        Assert.Equal(2, diagnoses.Count);
        Assert.Equal("Fever with chills", diagnoses[0].Text);
        Assert.Equal("Malaria", diagnoses[1].Text);
        Assert.Equal("* MALARIA", diagnoses[1].SourceText);
        Assert.Equal(95, diagnoses[1].Confidence);

        // Evidence linking is not optional on any child row (§12.3).
        Assert.All(diagnoses, d => Assert.Equal(documentId, d.DocumentId));
    }

    /// <summary>
    /// Re-merging replaces rather than duplicates, the same as every other row type — a prompt
    /// change can be re-run over stored extractions without cleaning up first.
    /// </summary>
    [Fact]
    public async Task ReMergingReplacesDiagnosesRatherThanDuplicatingThem()
    {
        var documentId = AddDocument("""
        { "diagnoses": [ { "text": "Jaundice", "sourceText": "Diagnosis: Jaundice" } ] }
        """);

        await _merger.MergeAsync(documentId);
        await _merger.MergeAsync(documentId);

        var diagnosis = Assert.Single(await _db.Diagnoses.ToListAsync());
        Assert.Equal("Jaundice", diagnosis.Text);
    }

    /// <summary>An entry naming no condition is not a row — it would show as a blank chip.</summary>
    [Fact]
    public async Task SkipsADiagnosisEntryWithNoText()
    {
        var documentId = AddDocument("""
        { "diagnoses": [ { "text": null, "sourceText": "illegible" }, { "text": "  " } ] }
        """);

        await _merger.MergeAsync(documentId);

        Assert.Empty(await _db.Diagnoses.ToListAsync());
    }

    /// <summary>
    /// The real hand-written label for `patient_x_year1_1`, merged as the pipeline would merge a
    /// stored extraction of it. The unit tests above use JSON written to prove a point; this one
    /// uses the file the golden gate scores against, so a schema drift between the label format
    /// and the merge shows up here rather than in a chat answer.
    ///
    /// This is the document behind the reported defect: `Diagnosis: MALARIA` above four drugs.
    /// </summary>
    [Fact]
    public async Task GoldenLabelWithADiagnosisSurvivesTheRoundTrip()
    {
        var label = Path.Combine(RepositoryRoot(), "dataset", "golden", "patient_x_year1_1.json");
        Assert.True(File.Exists(label), $"Golden label missing at {label}. It is committed, not gitignored.");

        var documentId = AddDocument(await File.ReadAllTextAsync(label));

        await _merger.MergeAsync(documentId);

        var diagnosis = Assert.Single(await _db.Diagnoses.ToListAsync());
        Assert.Equal("Malaria", diagnosis.Text);
        Assert.Equal("* MALARIA", diagnosis.SourceText);

        // The four drugs printed beneath it come through the same merge, which is what makes the
        // question answerable at all.
        Assert.Equal(4, await _db.Medications.CountAsync());
    }

    /// <summary>
    /// Anchored on this file's own path rather than the build output: the test assemblies are
    /// built to a separate artifacts directory when the API's bin is locked, and walking up from
    /// AppContext.BaseDirectory then leaves the repository entirely.
    /// </summary>
    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

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
