using MediTrail.Api.AiPipeline;
using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.AiPipeline.RuleChecks;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediTrail.Tests;

/// <summary>
/// What the patient-level stage writes once the checks have run — driven through
/// <see cref="PatientAnalyzer"/> itself, because the evidence a user sees is the evidence that
/// was persisted, not the evidence a checker proposed.
/// </summary>
public class PatientAnalyzerTests : IDisposable
{
    private readonly MediTrailDbContext _db;
    private readonly Guid _patientId = Guid.NewGuid();

    public PatientAnalyzerTests()
    {
        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseInMemoryDatabase($"analyze-{Guid.NewGuid()}")
            .Options;

        _db = new MediTrailDbContext(options);
        _db.Patients.Add(new Patient { Id = _patientId, DisplayName = "Test" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// The same scan uploaded twice is one piece of paper. Citing both copies made an alert card
    /// read "Evidence: 3.jpg 2.jpg 2.jpg", which says the problem was found in three places.
    /// </summary>
    [Fact]
    public async Task CollapsesEvidenceCopiesOfOneFileToTheEarliestUpload()
    {
        const string SharedHash = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

        var original = AddDocument(SharedHash, uploadedAt: new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero));
        var reupload = AddDocument(SharedHash, uploadedAt: new DateTimeOffset(2026, 8, 1, 9, 5, 0, TimeSpan.Zero));
        var other = AddDocument(new string('b', 64), uploadedAt: new DateTimeOffset(2026, 8, 1, 9, 10, 0, TimeSpan.Zero));

        await _db.SaveChangesAsync();

        var stored = await AnalyzeWithAsync(Finding([reupload, original, other]));

        Assert.Equal([original, other], stored.EvidenceDocumentIds);
    }

    /// <summary>
    /// The collapse key is the file hash and nothing else — two different pages handed over at the
    /// same visit are two documents the user may need to open.
    /// </summary>
    [Fact]
    public async Task KeepsDifferentFilesFromTheSameVisitSeparate()
    {
        var uploadedAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        var visitDate = new DateOnly(2023, 8, 30);

        var prescription = AddDocument(new string('a', 64), uploadedAt, visitDate);
        var labReport = AddDocument(new string('b', 64), uploadedAt, visitDate);

        await _db.SaveChangesAsync();

        var stored = await AnalyzeWithAsync(Finding([prescription, labReport]));

        Assert.Equal([prescription, labReport], stored.EvidenceDocumentIds);
    }

    // ---- fixtures ----

    /// <summary>Runs the full patient-level stage over a single rule finding and returns it as persisted.</summary>
    private async Task<Alert> AnalyzeWithAsync(Alert finding)
    {
        var analyzer = new PatientAnalyzer(
            _db,
            new NoOpMerger(),
            new StubRuleChecker(finding),
            new NoServices(),
            NullLogger<PatientAnalyzer>.Instance);

        await analyzer.AnalyzeAsync(_patientId);

        return Assert.Single(await _db.Alerts.AsNoTracking().Where(a => a.PatientId == _patientId).ToListAsync());
    }

    private Alert Finding(List<Guid> evidenceDocumentIds) =>
        new()
        {
            PatientId = _patientId,
            Type = AlertType.DocumentWarningConflict,
            Severity = AlertSeverity.Red,
            Title = "Aspirin was prescribed despite a warning on the same document",
            InvolvedGenerics = ["aspirin"],
            ExplanationEn = "Test finding.",
            Confidence = 90,
            RequiresProfessionalConsult = true,
            VerificationStatus = VerificationStatus.NotApplicable,
            EvidenceDocumentIds = evidenceDocumentIds,
            DetectedBy = "rules"
        };

    private Guid AddDocument(string sha256, DateTimeOffset uploadedAt, DateOnly? documentDate = null)
    {
        var id = Guid.NewGuid();

        _db.Documents.Add(new Document
        {
            Id = id,
            PatientId = _patientId,
            OriginalFileName = $"{id}.jpg",
            ContentType = "image/jpeg",
            StoragePath = $"{_patientId}/{id}.jpg",
            Sha256 = sha256,
            DocumentDate = documentDate ?? new DateOnly(2023, 8, 30),
            Status = DocumentStatus.Extracted,
            OverallConfidence = 90,
            CreatedAt = uploadedAt
        });

        return id;
    }

    /// <summary>The merge is exercised by its own tests; here the normalized rows are the fixture.</summary>
    private sealed class NoOpMerger : IExtractionMerger
    {
        public Task MergeAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubRuleChecker(params Alert[] alerts) : IRuleChecker
    {
        public Task<IReadOnlyList<Alert>> CheckAsync(Guid patientId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Alert>>(alerts);
    }

    /// <summary>No AI client configured, so the LLM cross-check is absent — as it is offline.</summary>
    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
