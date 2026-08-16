using MediTrail.Api.AiPipeline;
using MediTrail.Api.AiPipeline.CrossCheck;
using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.AiPipeline.Verification;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediTrail.Tests;

/// <summary>
/// The temporal gate on stage 4 (§11.1). The model is faked and always proposes the interaction —
/// what is under test is whether the pipeline lets a finding through for two medicines the patient
/// was never taking at the same time.
/// </summary>
public class InteractionCrossCheckerTests : IDisposable
{
    private readonly MediTrailDbContext _db;
    private readonly FakeAiClient _ai = new();
    private readonly Guid _patientId = Guid.NewGuid();

    public InteractionCrossCheckerTests()
    {
        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseInMemoryDatabase($"crosscheck-{Guid.NewGuid()}")
            .Options;

        _db = new MediTrailDbContext(options);
        _db.Patients.Add(new Patient { Id = _patientId, DisplayName = "Test" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// The noise this gate exists to remove: atenolol from a 2013 visit and methylphenidate from a
    /// 2023 one were never in the body together, whatever the model says about the pair.
    /// </summary>
    [Fact]
    public async Task DoesNotRaiseAnInteractionForPrescriptionsTenYearsApart()
    {
        var old = AddDocument(new DateOnly(2013, 4, 2));
        var recent = AddDocument(new DateOnly(2023, 4, 2));

        AddMedication(old, "atenolol", new DateOnly(2013, 4, 2), durationDays: 15);
        AddMedication(recent, "methylphenidate", new DateOnly(2023, 4, 2), durationDays: 24);

        await _db.SaveChangesAsync();

        Assert.Empty(await Checker().CheckAsync(_patientId));
    }

    [Fact]
    public async Task RaisesAnInteractionWhenTheWindowsOverlap()
    {
        var first = AddDocument(new DateOnly(2023, 4, 2));
        var second = AddDocument(new DateOnly(2023, 4, 10));

        AddMedication(first, "atenolol", new DateOnly(2023, 4, 2), durationDays: 15);
        AddMedication(second, "methylphenidate", new DateOnly(2023, 4, 10), durationDays: 24);

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await Checker().CheckAsync(_patientId));

        Assert.Equal(AlertType.DrugInteraction, alert.Type);
        Assert.Equal(["atenolol", "methylphenidate"], alert.InvolvedGenerics.Order());
        // Nothing to caveat: both dates read cleanly and the windows meet.
        Assert.DoesNotContain("no readable date", alert.ExplanationEn!);
    }

    /// <summary>
    /// A prescription with no printed duration and nothing saying it continues gets a conservative
    /// 30-day course, not an indefinite one — otherwise one undated repeat prescription pairs with
    /// everything that follows it forever.
    /// </summary>
    [Fact]
    public async Task TreatsAPrescriptionWithNoDurationAsAFixedCourse()
    {
        var first = AddDocument(new DateOnly(2023, 1, 1));
        var second = AddDocument(new DateOnly(2023, 6, 1));

        AddMedication(first, "atenolol", new DateOnly(2023, 1, 1), frequency: "1 od");
        AddMedication(second, "methylphenidate", new DateOnly(2023, 6, 1), frequency: "1 od");

        await _db.SaveChangesAsync();

        // Five months apart: inside an indefinite window, outside a 30-day one.
        Assert.Empty(await Checker().CheckAsync(_patientId));
    }

    /// <summary>
    /// "As and when required" is a drug with no stop date — the sublingual nitrate in the dataset.
    /// It stays open-ended, so a later prescription still pairs with it.
    /// </summary>
    [Fact]
    public async Task KeepsAnAsNeededPrescriptionActiveWithNoPrintedEnd()
    {
        var first = AddDocument(new DateOnly(2023, 1, 1));
        var second = AddDocument(new DateOnly(2023, 6, 1));

        AddMedication(first, "atenolol", new DateOnly(2023, 1, 1),
            frequency: "sublingually as and when required");
        AddMedication(second, "methylphenidate", new DateOnly(2023, 6, 1), frequency: "1 od");

        await _db.SaveChangesAsync();

        Assert.Single(await Checker().CheckAsync(_patientId));
    }

    /// <summary>
    /// An unreadable date must not silently delete a finding (traps.md Y10/Y11 produce exactly this
    /// — `Jan 9, 20yy` and `09-11-12` both normalize to null). The pair is kept and labelled.
    /// </summary>
    [Fact]
    public async Task KeepsThePairAndSaysSoWhenADocumentDateCouldNotBeRead()
    {
        var dated = AddDocument(new DateOnly(2013, 4, 2));
        var undated = AddDocument(null);

        AddMedication(dated, "atenolol", new DateOnly(2013, 4, 2), durationDays: 15);
        AddMedication(undated, "methylphenidate", start: null);

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await Checker().CheckAsync(_patientId));

        Assert.Contains("no readable date", alert.ExplanationEn!);
        // The Tamil explanation carries the same caveat, not an English sentence appended to Tamil.
        Assert.Contains(MedicationWindowCalculator.DateUnknownCaveatTa, alert.ExplanationTa!);
    }

    /// <summary>Two medicines on one prescription are concurrent by definition (traps.md Y4, Y8).</summary>
    [Fact]
    public async Task AlwaysChecksTwoMedicinesFromTheSameDocument()
    {
        var documentId = AddDocument(null);

        AddMedication(documentId, "atenolol", start: null);
        AddMedication(documentId, "methylphenidate", start: null);

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await Checker().CheckAsync(_patientId));
        // Same document, so concurrency is not in doubt and there is nothing to caveat.
        Assert.DoesNotContain("no readable date", alert.ExplanationEn!);
    }

    /// <summary>The model is grounded on the windows, not just the prescribing month.</summary>
    [Fact]
    public async Task ShowsEachMedicationsActiveWindowToTheModel()
    {
        var documentId = AddDocument(new DateOnly(2023, 4, 2));

        AddMedication(documentId, "atenolol", new DateOnly(2023, 4, 2), durationDays: 15);
        AddMedication(documentId, "methylphenidate", start: null);

        await _db.SaveChangesAsync();

        await Checker().CheckAsync(_patientId);

        Assert.Contains("2023-04-02 to 2023-04-16", _ai.LastPrompt);
        Assert.Contains("date not readable", _ai.LastPrompt);
    }

    // ---- fixtures ----

    private InteractionCrossChecker Checker() =>
        new(_db, _ai, new FakePromptLibrary(), new FakeOpenFdaClient(),
            NullLogger<InteractionCrossChecker>.Instance);

    /// <summary>
    /// traps.md X1, in the shape the pipeline actually produced it.
    ///
    /// `patient_x_year3_2` prints "CAP. ASPIRIN AND CODEINE", which merges as the combination
    /// generic `aspirin/codeine`. The model proposed warfarin + aspirin — the strongest and
    /// best-documented interaction in the dataset — and the grounding lookup, being whole-string,
    /// found no `aspirin` key and dropped it. The only trace was a Debug line.
    /// </summary>
    [Fact]
    public async Task GroundsAnInteractionNamingOneIngredientOfACombinationProduct()
    {
        _ai.GenericA = "warfarin";
        _ai.GenericB = "aspirin";

        var document = AddDocument(new DateOnly(2019, 8, 5));

        AddMedication(document, "warfarin", new DateOnly(2019, 8, 5), durationDays: 5);
        AddMedication(document, "aspirin/codeine", new DateOnly(2019, 8, 5), durationDays: 5);

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await Checker().CheckAsync(_patientId));

        Assert.Equal(AlertType.DrugInteraction, alert.Type);

        // Named as the record holds it, so the medications table and the evidence viewer can find
        // the row, and the reader can see which product on their own list carries the aspirin.
        Assert.Equal(["aspirin/codeine", "warfarin"], alert.InvolvedGenerics.Order());
        Assert.Contains(document, alert.EvidenceDocumentIds);
    }

    /// <summary>
    /// The other half of component matching: naming two ingredients of one tablet describes one
    /// product, not two drugs, and "Aspirin and Codeine may interact" about a single tablet would
    /// be a finding invented by the widened lookup rather than found by it.
    /// </summary>
    [Fact]
    public async Task DoesNotRaiseAnInteractionBetweenTwoIngredientsOfTheSameProduct()
    {
        _ai.GenericA = "aspirin";
        _ai.GenericB = "codeine";

        var document = AddDocument(new DateOnly(2019, 8, 5));

        AddMedication(document, "aspirin/codeine", new DateOnly(2019, 8, 5), durationDays: 5);
        AddMedication(document, "warfarin", new DateOnly(2019, 8, 5), durationDays: 5);

        await _db.SaveChangesAsync();

        Assert.Empty(await Checker().CheckAsync(_patientId));
    }

    private Guid AddDocument(DateOnly? date)
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
            DocumentDate = date,
            Status = DocumentStatus.Extracted
        });

        return id;
    }

    private void AddMedication(Guid documentId, string generic, DateOnly? start = null,
        int? durationDays = null, string? frequency = null) =>
        _db.Medications.Add(new Medication
        {
            PatientId = _patientId,
            DocumentId = documentId,
            GenericName = generic,
            Frequency = frequency,
            DurationDays = durationDays,
            StartDate = start,
            // As the merger derives it: start + duration, or null when no duration was printed.
            EndDate = start is not null && durationDays is > 0
                ? start.Value.AddDays(durationDays.Value - 1)
                : null,
            Confidence = 90
        });

    /// <summary>
    /// Always proposes the one interaction, so only the pipeline decides the outcome. The pair is
    /// settable because grounding is what some of these tests are about: the model naming a drug
    /// the record holds only inside a combination product is the whole of traps.md X1.
    /// </summary>
    private sealed class FakeAiClient : IAiClient
    {
        public string LastPrompt { get; private set; } = string.Empty;

        public string GenericA { get; set; } = "atenolol";
        public string GenericB { get; set; } = "methylphenidate";
        public string Severity { get; set; } = "amber";

        public Task<AiCompletion> CompleteAsync(
            string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
        {
            LastPrompt = systemPrompt;

            return Task.FromResult(new AiCompletion
            {
                Content = $$"""
                {
                  "findings": [
                    {
                      "genericA": "{{GenericA}}",
                      "genericB": "{{GenericB}}",
                      "severity": "{{Severity}}",
                      "explanationEn": "These two can pull your heart rate in opposite directions.",
                      "explanationTa": "இவை இரண்டும் இதயத் துடிப்பை எதிரெதிர் திசையில் மாற்றக்கூடும்.",
                      "suggestedActionEn": "Ask your pharmacist whether both are still needed.",
                      "suggestedActionTa": "இரண்டும் தேவையா என்று மருந்தாளரிடம் கேளுங்கள்.",
                      "confidence": 85
                    }
                  ]
                }
                """,
                Model = "fake",
                LatencyMs = 1
            });
        }

        public Task<AiCompletion> CompleteWithImagesAsync(
            string systemPrompt, IReadOnlyList<byte[]> images, string imageContentType,
            string? model = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePromptLibrary : IPromptLibrary
    {
        public string Get(string name) => name;

        public string Get(string name, IReadOnlyDictionary<string, string> placeholders) =>
            string.Join("\n", placeholders.Values);
    }

    /// <summary>openFDA is an enhancement, never a gate — unreachable here, findings stand.</summary>
    private sealed class FakeOpenFdaClient : IOpenFdaClient
    {
        public Task<FdaVerification> VerifyInteractionAsync(
            string genericName, string interactsWith, CancellationToken ct = default) =>
            Task.FromResult(FdaVerification.NotConfirmed());

        public Task<bool> GenericExistsAsync(string genericName, CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}
