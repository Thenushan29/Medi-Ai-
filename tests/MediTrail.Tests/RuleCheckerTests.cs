using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.AiPipeline.RuleChecks;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediTrail.Tests;

/// <summary>
/// The deterministic cross-checks, exercised against the scenarios actually planted in the
/// evaluation dataset (dataset/golden/traps.md).
/// </summary>
public class RuleCheckerTests : IDisposable
{
    private readonly MediTrailDbContext _db;
    private readonly DeterministicRuleChecker _checker;
    private readonly Guid _patientId = Guid.NewGuid();

    public RuleCheckerTests()
    {
        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseInMemoryDatabase($"rules-{Guid.NewGuid()}")
            .Options;

        _db = new MediTrailDbContext(options);
        _db.Patients.Add(new Patient { Id = _patientId, DisplayName = "Test" });
        _db.SaveChanges();

        _checker = new DeterministicRuleChecker(_db, NullLogger<DeterministicRuleChecker>.Instance);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// **The headline trap (traps.md Y1).** A jaundice prescription lists Paracetamol while its own
    /// advice section warns against acetaminophen. Detecting it requires the warning to be extracted
    /// with its generic, and paracetamol ≡ acetaminophen to hold through normalization.
    /// </summary>
    [Fact]
    public async Task DetectsSameDocumentContradiction_ParacetamolVersusAcetaminophenWarning()
    {
        var documentId = Guid.NewGuid();
        AddDocument(documentId, new DateOnly(2025, 11, 1));

        AddMedication(documentId, generic: "paracetamol", brand: "Crocin", strength: 500, perDay: 4);

        AddWarning(documentId,
            text: "Avoid taking unnecessary or liver-toxic medications (e.g. alcohol, acetaminophen)",
            relatesTo: ["acetaminophen"]);

        await _db.SaveChangesAsync();

        var alerts = await _checker.CheckAsync(_patientId);

        var alert = Assert.Single(alerts, a => a.Type == AlertType.DocumentWarningConflict);

        Assert.Equal(AlertSeverity.Red, alert.Severity);
        Assert.True(alert.RequiresProfessionalConsult);
        Assert.Contains("paracetamol", alert.InvolvedGenerics);

        // Both sides of the contradiction must be cited, and it must say the two names are the
        // same medicine — a reader who does not know that cannot see the contradiction.
        Assert.Contains(documentId, alert.EvidenceDocumentIds);
        Assert.Contains("same document", alert.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same medicine", alert.ExplanationEn!, StringComparison.OrdinalIgnoreCase);

        // The explanation quotes the printed sentence, not the substance column — a reader shown
        // only "acetaminophen" cannot see what the document actually advised.
        Assert.Contains("liver-toxic", alert.ExplanationEn!, StringComparison.OrdinalIgnoreCase);

        // The demo finding is rule-detected, so its Tamil comes from the templates or not at all
        // (FR-5.8). The quoted warning and both drug names carry over.
        Assert.Contains("Paracetamol", alert.ExplanationTa!, StringComparison.Ordinal);
        Assert.Contains("liver-toxic", alert.ExplanationTa!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// FR-5.8 and Principle 6: a rule finding is bilingual by construction. Nothing downstream
    /// fills <c>ExplanationTa</c> in for the deterministic checks, so an alert without it renders
    /// the "Tamil not available yet" fallback forever.
    /// </summary>
    [Fact]
    public async Task RuleDetectedAllergyConflictCarriesATamilExplanation()
    {
        var allergyDoc = Guid.NewGuid();
        var rxDoc = Guid.NewGuid();
        AddDocument(allergyDoc, new DateOnly(2019, 8, 5));
        AddDocument(rxDoc, new DateOnly(2011, 7, 15));

        AddAllergy(allergyDoc, substance: "Penicillin", reaction: "rash");
        AddMedication(rxDoc, generic: "amoxicillin", perDay: 2);

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await _checker.CheckAsync(_patientId),
            a => a.Type == AlertType.AllergyConflict);

        Assert.False(string.IsNullOrWhiteSpace(alert.ExplanationTa));

        // Drug names stay in their printed form inside the Tamil sentence — transliterating them
        // would leave the reader unable to match the word to the box in their hand.
        Assert.Contains("Amoxicillin", alert.ExplanationTa!, StringComparison.Ordinal);
        Assert.Contains("Penicillin", alert.ExplanationTa!, StringComparison.Ordinal);

        // Tamil, not the English sentence copied into the Tamil column.
        Assert.NotEqual(alert.ExplanationEn, alert.ExplanationTa);
    }

    /// <summary>
    /// traps.md Y3 — three beta-blockers whose generic names all differ, in the shape the dataset
    /// actually has them: atenolol on the dated 2007 prescription, metoprolol and oxprenolol on
    /// `y_year3_6`, whose printed date (`09-11-12`) is ambiguous and normalizes to null (Y11).
    ///
    /// The temporal gate must not turn an unreadable date into a silently dropped finding: the
    /// three still cluster, and the alert says the concurrency could not be established.
    /// </summary>
    [Fact]
    public async Task DetectsDuplicateTherapeuticClass_ThreeBetaBlockers()
    {
        var dated = Guid.NewGuid();
        var undated = Guid.NewGuid();
        AddDocument(dated, new DateOnly(2007, 7, 7));
        AddDocument(undated, null);

        AddMedication(dated, generic: "atenolol", strength: 50, perDay: 1);
        AddMedication(undated, generic: "metoprolol", strength: 100, perDay: 2);
        AddMedication(undated, generic: "oxprenolol", strength: 50, perDay: 1);

        await _db.SaveChangesAsync();

        var alerts = await _checker.CheckAsync(_patientId);

        var alert = Assert.Single(alerts, x => x.Type == AlertType.DuplicatePrescription
                                            && x.InvolvedGenerics.Count == 3);

        Assert.Equal(AlertSeverity.Red, alert.Severity);
        Assert.Contains("beta blocker", alert.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, alert.EvidenceDocumentIds.Count);

        Assert.Contains("no readable date", alert.ExplanationEn!);
        Assert.Contains(MedicationWindowCalculator.DateUnknownCaveatTa, alert.ExplanationTa!);
    }

    /// <summary>
    /// traps.md Y3, in the shape the pipeline actually produced it rather than the shape the test
    /// above assumes.
    ///
    /// The run recorded in traps.md merged `Oxprelol 50mg` with `genericName: null`, because the
    /// model correctly declined to resolve an ambiguous misspelling (Y12) and the brand table did
    /// not know it either. A null generic is excluded from every cross-check, so the class alert
    /// named two beta-blockers instead of three — a finding lost to a spelling, with no signal.
    ///
    /// The third generic here comes from the brand fallback, which is the only route available
    /// when the model returns null: if the table forgets `oxprelol`, this fails.
    /// </summary>
    [Fact]
    public async Task ThirdBetaBlockerReachesTheClassCheckThroughTheBrandFallback()
    {
        var oxprenolol = DrugNameNormalizer.GenericForBrand("Oxprelol 50mg");
        Assert.Equal("oxprenolol", oxprenolol);

        var dated = Guid.NewGuid();
        var undated = Guid.NewGuid();
        AddDocument(dated, new DateOnly(2007, 7, 7));
        AddDocument(undated, null);

        AddMedication(dated, generic: "atenolol", strength: 50, perDay: 1);
        AddMedication(undated, generic: "metoprolol", brand: "Betaloc", strength: 100, perDay: 2);
        AddMedication(undated, generic: oxprenolol!, brand: "Oxprelol", strength: 50, perDay: 1);

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await _checker.CheckAsync(_patientId),
            x => x.Type == AlertType.DuplicatePrescription);

        Assert.Equal(AlertSeverity.Red, alert.Severity);
        Assert.Contains("beta blocker", alert.Title, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            new[] { "atenolol", "metoprolol", "oxprenolol" },
            alert.InvolvedGenerics.Order().ToArray());
    }

    /// <summary>
    /// The same-document contradiction, with the medication written as a combination product.
    ///
    /// This is traps.md X1's shape on the safety side: whole-string equality cannot see the
    /// paracetamol inside `ibuprofen/paracetamol`, so a page prescribing it under a printed
    /// warning against acetaminophen produced nothing at all. The dataset happens to print plain
    /// Paracetamol; a judge's dataset printing Combiflam would have gone unreported.
    /// </summary>
    [Fact]
    public async Task MatchesAPrintedWarningAgainstAnIngredientOfACombinationProduct()
    {
        var documentId = Guid.NewGuid();
        AddDocument(documentId, new DateOnly(2025, 11, 1));

        AddMedication(documentId, generic: "ibuprofen/paracetamol", brand: "Combiflam", strength: 400);
        AddWarning(documentId,
            "Avoid taking unnecessary or liver-toxic medications (e.g. alcohol, acetaminophen).",
            ["paracetamol"]);

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await _checker.CheckAsync(_patientId),
            a => a.Type == AlertType.DocumentWarningConflict);

        Assert.Equal(AlertSeverity.Red, alert.Severity);
        Assert.True(alert.RequiresProfessionalConsult);
        Assert.Contains("same document", alert.Title);
        Assert.Equal(["ibuprofen/paracetamol"], alert.InvolvedGenerics);
    }

    /// <summary>
    /// A combination product takes its class from its ingredients, so two products that both
    /// contain an NSAID are duplicate therapy even though neither generic string is in the table.
    /// </summary>
    [Fact]
    public async Task ClustersACombinationProductIntoItsIngredientsTherapeuticClass()
    {
        var documentId = Guid.NewGuid();
        AddDocument(documentId, new DateOnly(2019, 8, 5));

        AddMedication(documentId, generic: "aspirin/codeine", strength: 325);
        AddMedication(documentId, generic: "ibuprofen", strength: 400);

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await _checker.CheckAsync(_patientId),
            a => a.Type == AlertType.DuplicatePrescription);

        Assert.Contains("nsaid", alert.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["aspirin/codeine", "ibuprofen"], alert.InvolvedGenerics.Order());
    }

    /// <summary>
    /// The gap Task 6 exists to close: an unresolved generic takes part in no check, and used to
    /// do so in silence. Confidence 90 on the document means the low-confidence alert cannot cover
    /// it — this is a naming failure, not a legibility one.
    /// </summary>
    [Fact]
    public async Task ReportsAMedicationWhoseActiveIngredientCouldNotBeIdentified()
    {
        var documentId = Guid.NewGuid();
        AddDocument(documentId, new DateOnly(2012, 11, 9));

        AddMedication(documentId, generic: null!, brand: "SM FIBRO");

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await _checker.CheckAsync(_patientId),
            a => a.Type == AlertType.UnresolvedMedication);

        Assert.Equal(AlertSeverity.Info, alert.Severity);
        Assert.Contains("SM FIBRO", alert.Title);
        Assert.Contains("left out of the", alert.ExplanationEn!);
        Assert.Equal([documentId], alert.EvidenceDocumentIds);

        // Bilingual by construction, not a copy of the English (Principle 6).
        Assert.NotNull(alert.ExplanationTa);
        Assert.NotEqual(alert.ExplanationEn, alert.ExplanationTa);
    }

    /// <summary>
    /// A placeholder has no generic because it is not a drug (traps.md X6), which is a different
    /// fact from "we could not identify this". Reporting the four sample documents' fourteen
    /// placeholder rows as unidentified medicines would bury the one row that matters.
    /// </summary>
    [Fact]
    public async Task DoesNotReportPlaceholderRowsAsUnidentifiedMedications()
    {
        var documentId = Guid.NewGuid();
        AddDocument(documentId, new DateOnly(2020, 4, 27));

        AddMedication(documentId, generic: null!, brand: "DEMO MEDICINE 1");
        AddMedication(documentId, generic: null!, brand: "DEMO MEDICINE 2");

        await _db.SaveChangesAsync();

        Assert.DoesNotContain(await _checker.CheckAsync(_patientId),
            a => a.Type == AlertType.UnresolvedMedication);
    }

    /// <summary>
    /// Two beta-blockers on one page (traps.md Y4) need no date reasoning at all — one prescription
    /// is one prescribing decision.
    /// </summary>
    [Fact]
    public async Task DetectsTwoBetaBlockersOnOneUndatedPrescriptionWithoutCaveat()
    {
        var documentId = Guid.NewGuid();
        AddDocument(documentId, null);

        AddMedication(documentId, generic: "metoprolol", strength: 100, perDay: 2);
        AddMedication(documentId, generic: "oxprenolol", strength: 50, perDay: 1);

        await _db.SaveChangesAsync();

        var alert = Assert.Single(await _checker.CheckAsync(_patientId),
            x => x.Type == AlertType.DuplicatePrescription);

        Assert.Equal(2, alert.InvolvedGenerics.Count);
        Assert.DoesNotContain("no readable date", alert.ExplanationEn!);
    }

    /// <summary>
    /// A beta-blocker stopped in 2007 and another started in 2019 is a change of therapy, not two
    /// at once. The finding claims the patient may be taking both — with dated, non-overlapping
    /// courses that claim is simply false.
    /// </summary>
    [Fact]
    public async Task DoesNotReportTherapeuticClassDuplicateAcrossNonOverlappingYears()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        AddDocument(first, new DateOnly(2007, 7, 7));
        AddDocument(second, new DateOnly(2019, 8, 5));

        AddMedication(first, generic: "atenolol", strength: 50, perDay: 1,
            start: new DateOnly(2007, 7, 7), end: new DateOnly(2007, 7, 21));
        AddMedication(second, generic: "metoprolol", strength: 100, perDay: 2,
            start: new DateOnly(2019, 8, 5), end: new DateOnly(2019, 8, 19));

        await _db.SaveChangesAsync();

        Assert.DoesNotContain(await _checker.CheckAsync(_patientId),
            a => a.Type == AlertType.DuplicatePrescription);
    }

    /// <summary>A penicillin allergy has to catch amoxicillin, which is not the same generic.</summary>
    [Fact]
    public async Task DetectsAllergyConflictThroughDrugClass()
    {
        var allergyDoc = Guid.NewGuid();
        var rxDoc = Guid.NewGuid();
        AddDocument(allergyDoc, new DateOnly(2019, 8, 5));
        AddDocument(rxDoc, new DateOnly(2011, 7, 15));

        AddAllergy(allergyDoc, substance: "Penicillin", reaction: "rash");
        AddMedication(rxDoc, generic: "amoxicillin", perDay: 2);

        await _db.SaveChangesAsync();

        var alerts = await _checker.CheckAsync(_patientId);

        var alert = Assert.Single(alerts, a => a.Type == AlertType.AllergyConflict);
        Assert.Equal(AlertSeverity.Red, alert.Severity);
        Assert.Contains("rash", alert.ExplanationEn!);
    }

    [Fact]
    public async Task DetectsDuplicatePrescriptionAcrossOverlappingVisits()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        AddDocument(a, new DateOnly(2023, 8, 30));
        AddDocument(b, new DateOnly(2023, 9, 1));

        AddMedication(a, generic: "clarithromycin", strength: 500, perDay: 1, start: new DateOnly(2023, 8, 30), end: new DateOnly(2023, 9, 6));
        AddMedication(b, generic: "clarithromycin", strength: 500, perDay: 1, start: new DateOnly(2023, 9, 1), end: new DateOnly(2023, 9, 8));

        await _db.SaveChangesAsync();

        var alerts = await _checker.CheckAsync(_patientId);

        Assert.Contains(alerts, x => x.Type == AlertType.DuplicatePrescription
                                  && x.InvolvedGenerics.SequenceEqual(new[] { "clarithromycin" }));
    }

    /// <summary>
    /// traps.md Y2 — the dataset contains the same file twice. Two rows from one document, or from
    /// a re-upload of the same visit, are one prescribing decision, not double-dosing.
    /// </summary>
    [Fact]
    public async Task DoesNotReportDuplicateWithinASingleDocument()
    {
        var documentId = Guid.NewGuid();
        AddDocument(documentId, new DateOnly(2007, 7, 7));

        AddMedication(documentId, generic: "aspirin", strength: 100, perDay: 1);
        AddMedication(documentId, generic: "aspirin", strength: 100, perDay: 1);

        await _db.SaveChangesAsync();

        var alerts = await _checker.CheckAsync(_patientId);

        Assert.DoesNotContain(alerts, a => a.Type == AlertType.DuplicatePrescription);
    }

    /// <summary>
    /// traps.md Y2, the case that actually occurs: a person uploads a folder and the same scan is
    /// in it twice. Two separate document rows, identical bytes — one prescribing event, so no
    /// duplicate and no dosage conflict between them.
    /// </summary>
    [Fact]
    public async Task DoesNotReportDuplicateBetweenByteIdenticalUploads()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        const string SharedHash = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

        AddDocument(first, new DateOnly(2007, 7, 7), sha256: SharedHash);
        AddDocument(second, new DateOnly(2007, 7, 7), sha256: SharedHash);

        AddMedication(first, generic: "atenolol", strength: 50, perDay: 1);
        AddMedication(second, generic: "atenolol", strength: 50, perDay: 1);

        await _db.SaveChangesAsync();

        var alerts = await _checker.CheckAsync(_patientId);

        Assert.DoesNotContain(alerts, a => a.Type == AlertType.DuplicatePrescription);
        Assert.DoesNotContain(alerts, a => a.Type == AlertType.DosageConflict);
    }

    /// <summary>
    /// The same case as it actually reaches the checker after a re-upload: the second document is
    /// marked <see cref="DocumentStatus.Cached"/>, its extraction reused rather than re-billed
    /// (FR-2.6). Status must make no difference — the file hash is what decides.
    /// </summary>
    [Fact]
    public async Task DoesNotReportDuplicateWhenTheSecondDocumentWasServedFromCache()
    {
        var original = Guid.NewGuid();
        var reupload = Guid.NewGuid();
        const string SharedHash = "f0e1d2c3b4a59687f0e1d2c3b4a59687f0e1d2c3b4a59687f0e1d2c3b4a59687";

        AddDocument(original, new DateOnly(2023, 8, 30), sha256: SharedHash);
        AddDocument(reupload, new DateOnly(2023, 8, 30), sha256: SharedHash,
            status: DocumentStatus.Cached);

        AddMedication(original, generic: "clarithromycin", strength: 500, perDay: 2,
            start: new DateOnly(2023, 8, 30), end: new DateOnly(2023, 9, 6));
        AddMedication(reupload, generic: "clarithromycin", strength: 500, perDay: 2,
            start: new DateOnly(2023, 8, 30), end: new DateOnly(2023, 9, 6));

        await _db.SaveChangesAsync();

        Assert.DoesNotContain(await _checker.CheckAsync(_patientId),
            a => a.Type == AlertType.DuplicatePrescription);
    }

    /// <summary>The suppression must be by content, not by date — different files still count.</summary>
    [Fact]
    public async Task StillReportsDuplicateBetweenDifferentFilesOnTheSameDay()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        AddDocument(first, new DateOnly(2007, 7, 7), sha256: new string('a', 64));
        AddDocument(second, new DateOnly(2007, 7, 7), sha256: new string('b', 64));

        AddMedication(first, generic: "atenolol", strength: 50, perDay: 1);
        AddMedication(second, generic: "atenolol", strength: 50, perDay: 1);

        await _db.SaveChangesAsync();

        Assert.Contains(await _checker.CheckAsync(_patientId), a => a.Type == AlertType.DuplicatePrescription);
    }

    [Fact]
    public async Task DetectsDosageConflictAcrossDocuments()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        AddDocument(a, new DateOnly(2023, 1, 1));
        AddDocument(b, new DateOnly(2023, 6, 1));

        AddMedication(a, generic: "atenolol", strength: 50, perDay: 1);
        AddMedication(b, generic: "atenolol", strength: 100, perDay: 2);

        await _db.SaveChangesAsync();

        var alerts = await _checker.CheckAsync(_patientId);

        var alert = Assert.Single(alerts, x => x.Type == AlertType.DosageConflict);
        Assert.True(alert.RequiresProfessionalConsult);
    }

    [Fact]
    public async Task FlagsLabValueOutsideThePrintedRange()
    {
        var documentId = Guid.NewGuid();
        AddDocument(documentId, new DateOnly(2023, 4, 10));

        _db.LabResults.Add(new LabResult
        {
            PatientId = _patientId,
            DocumentId = documentId,
            TestName = "SGPT (ALT)",
            TestNameStandard = "alt",
            ValueNumeric = 88,
            Unit = "U/L",
            NormalMin = 7,
            NormalMax = 56,
            NormalRangeText = "7 - 56",
            TestDate = new DateOnly(2023, 4, 10),
            IsOutOfRange = true,
            Confidence = 90
        });

        await _db.SaveChangesAsync();

        var alerts = await _checker.CheckAsync(_patientId);

        var alert = Assert.Single(alerts, a => a.Type == AlertType.LabOutOfRange);
        Assert.Contains("above", alert.ExplanationEn!);
        Assert.Contains("7 - 56", alert.ExplanationEn!);
    }

    /// <summary>A record with nothing wrong in it must produce nothing — no invented findings.</summary>
    [Fact]
    public async Task ProducesNoAlertsForACleanRecord()
    {
        var documentId = Guid.NewGuid();
        AddDocument(documentId, new DateOnly(2023, 4, 10));
        AddMedication(documentId, generic: "silymarin", strength: 140, perDay: 2);

        await _db.SaveChangesAsync();

        Assert.Empty(await _checker.CheckAsync(_patientId));
    }

    // ---- fixtures ----

    private void AddDocument(Guid id, DateOnly? date, string? sha256 = null,
        DocumentStatus status = DocumentStatus.Extracted) =>
        _db.Documents.Add(new Document
        {
            Id = id,
            PatientId = _patientId,
            OriginalFileName = $"{id}.png",
            ContentType = "image/png",
            StoragePath = $"{_patientId}/{id}.png",
            // Distinct by default, so ordinary fixtures are treated as different files.
            Sha256 = sha256 ?? id.ToString("N") + id.ToString("N"),
            DocumentDate = date,
            Status = status
        });

    private void AddMedication(Guid documentId, string generic, string? brand = null,
        decimal? strength = null, decimal? perDay = null, DateOnly? start = null, DateOnly? end = null)
    {
        var documentDate = _db.Documents.Local.First(d => d.Id == documentId).DocumentDate;

        _db.Medications.Add(new Medication
        {
            PatientId = _patientId,
            DocumentId = documentId,
            GenericName = generic,
            BrandName = brand,
            StrengthValue = strength,
            StrengthUnit = strength is null ? null : "mg",
            FrequencyPerDay = perDay,
            StartDate = start ?? documentDate,
            EndDate = end ?? start ?? documentDate,
            Confidence = 90
        });
    }

    /// <summary>
    /// Shaped as the merger writes it: the substance column names the substance, the printed
    /// sentence is the evidence in SourceText.
    /// </summary>
    private void AddWarning(Guid documentId, string text, List<string> relatesTo) =>
        _db.Allergies.Add(new Allergy
        {
            PatientId = _patientId,
            DocumentId = documentId,
            IsDocumentWarning = true,
            Substance = string.Join(", ", relatesTo),
            SubstanceGeneric = relatesTo.Count == 1 ? relatesTo[0] : null,
            RelatesTo = relatesTo,
            SourceText = text,
            Confidence = 85
        });

    private void AddAllergy(Guid documentId, string substance, string? reaction = null) =>
        _db.Allergies.Add(new Allergy
        {
            PatientId = _patientId,
            DocumentId = documentId,
            IsDocumentWarning = false,
            Substance = substance,
            SubstanceGeneric = substance.ToLowerInvariant(),
            RelatesTo = [substance.ToLowerInvariant()],
            Reaction = reaction,
            Confidence = 90
        });
}
