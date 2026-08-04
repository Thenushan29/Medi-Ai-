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
    }

    /// <summary>traps.md Y3 — three beta-blockers whose generic names all differ.</summary>
    [Fact]
    public async Task DetectsDuplicateTherapeuticClass_ThreeBetaBlockers()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        AddDocument(a, new DateOnly(2007, 7, 7));
        AddDocument(b, new DateOnly(2012, 11, 9));

        AddMedication(a, generic: "atenolol", strength: 50, perDay: 1);
        AddMedication(b, generic: "metoprolol", strength: 100, perDay: 2);
        AddMedication(b, generic: "oxprenolol", strength: 50, perDay: 1);

        await _db.SaveChangesAsync();

        var alerts = await _checker.CheckAsync(_patientId);

        var alert = Assert.Single(alerts, x => x.Type == AlertType.DuplicatePrescription
                                            && x.InvolvedGenerics.Count == 3);

        Assert.Equal(AlertSeverity.Red, alert.Severity);
        Assert.Contains("beta blocker", alert.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, alert.EvidenceDocumentIds.Count);
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

    private void AddDocument(Guid id, DateOnly date) =>
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

    private void AddWarning(Guid documentId, string text, List<string> relatesTo) =>
        _db.Allergies.Add(new Allergy
        {
            PatientId = _patientId,
            DocumentId = documentId,
            IsDocumentWarning = true,
            Substance = text,
            RelatesTo = relatesTo,
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
