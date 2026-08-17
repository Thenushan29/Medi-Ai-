using MediTrail.Api.Contracts.Api;
using System.Text.RegularExpressions;
using MediTrail.Api.AiPipeline;
using MediTrail.Api.AiPipeline.Chat;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediTrail.Tests;

/// <summary>
/// Stage 7 (§11.1): what the chat model is actually given to reason over.
///
/// The model is stubbed by a reader that answers strictly from the record it is handed — it can
/// only report a contradiction the context actually contains. That is the part under test: a
/// warning row that never reaches the record, or reaches it mangled, produces the "not found"
/// answer this suite exists to prevent (FR-7.1, FR-7.5).
/// </summary>
public partial class GroundedChatServiceTests : IDisposable
{
    private readonly MediTrailDbContext _db;
    private readonly RecordReadingAiClient _ai = new();

    public GroundedChatServiceTests()
    {
        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseInMemoryDatabase($"chat-{Guid.NewGuid()}")
            .Options;

        _db = new MediTrailDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// The reported defect: five warning rows on the record, an alert on the dashboard naming the
    /// contradiction, and chat answering that it had no allergy information.
    /// </summary>
    [Fact]
    public async Task AnswersFromADocumentWarningInsteadOfReportingNotFound()
    {
        var (patientId, documentId) = SeedWarningAndMatchingMedication(
            substance: "paracetamol",
            warning: "Avoid taking unnecessary or liver-toxic medications (eg.alcohol,\nacetaminophen).");

        await _db.SaveChangesAsync();

        var answer = await Service().AskAsync(patientId, "Was any medicine prescribed that I am allergic to?");

        Assert.True(answer.FoundInDocuments);
        Assert.Contains("paracetamol", answer.AnswerEn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(documentId, answer.Citations);
    }

    /// <summary>
    /// The same path with a different patient and a different substance — the fix is in how the
    /// record is assembled, and cannot be tied to one drug or one record.
    /// </summary>
    [Fact]
    public async Task AnswersTheSameWayForADifferentPatientAndSubstance()
    {
        var (patientId, documentId) = SeedWarningAndMatchingMedication(
            substance: "ibuprofen",
            warning: "Do not take ibuprofen — patient has a history of gastric bleeding.");

        await _db.SaveChangesAsync();

        var answer = await Service().AskAsync(patientId, "Is there anything here I should not be taking?");

        Assert.True(answer.FoundInDocuments);
        Assert.Contains("ibuprofen", answer.AnswerEn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(documentId, answer.Citations);
    }

    /// <summary>A recorded patient allergy has to reach the record on the same footing (FR-5.4).</summary>
    [Fact]
    public async Task IncludesRecordedPatientAllergiesAsWellAsPrintedWarnings()
    {
        var (patientId, _) = SeedWarningAndMatchingMedication(
            substance: "penicillin",
            warning: "Allergic to penicillin.",
            isDocumentWarning: false);

        await _db.SaveChangesAsync();

        var answer = await Service().AskAsync(patientId, "Was any medicine prescribed that I am allergic to?");

        Assert.True(answer.FoundInDocuments);
        Assert.Contains("penicillin", answer.AnswerEn, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Source text is transcribed from a page and carries its line breaks. Left in, the entry split
    /// across lines and its tail read as a separate, malformed one.
    /// </summary>
    [Fact]
    public async Task KeepsAnItemOnOneLineWhenItsSourceTextWraps()
    {
        var (patientId, _) = SeedWarningAndMatchingMedication(
            substance: "paracetamol",
            warning: "Avoid taking unnecessary or liver-toxic medications (eg.alcohol,\nacetaminophen).");

        await _db.SaveChangesAsync();

        await Service().AskAsync(patientId, "anything to avoid?");

        Assert.Contains(
            "- Warning printed on this document — do not take paracetamol: " +
            "\"Avoid taking unnecessary or liver-toxic medications (eg.alcohol, acetaminophen).\"",
            _ai.Prompt);
    }

    /// <summary>
    /// A finding the pipeline already confirmed is worth more than one the model re-derives — but
    /// only if it can cite it. A citation the grounding check cannot match is dropped, and an
    /// answer with no citation is indistinguishable from a guess (Principle 3).
    /// </summary>
    [Fact]
    public async Task ListsConfirmedFindingsWithTheirDocumentIds()
    {
        var (patientId, documentId) = SeedWarningAndMatchingMedication(
            substance: "paracetamol",
            warning: "Avoid liver-toxic medications.");

        _db.Alerts.Add(new Alert
        {
            PatientId = patientId,
            Type = AlertType.DocumentWarningConflict,
            Severity = AlertSeverity.Red,
            Title = "Paracetamol was prescribed despite a warning on the same document",
            InvolvedGenerics = ["paracetamol"],
            ExplanationEn = "This document prescribes Paracetamol, while its own advice section warns against it.",
            Confidence = 90,
            RequiresProfessionalConsult = true,
            VerificationStatus = VerificationStatus.NotApplicable,
            EvidenceDocumentIds = [documentId],
            DetectedBy = "rules"
        });

        await _db.SaveChangesAsync();

        await Service().AskAsync(patientId, "anything to avoid?");

        Assert.Contains("## Findings already raised by the system", _ai.Prompt);
        Assert.Contains("including when the warning is on the SAME document", _ai.Prompt);
        Assert.Contains($"(documents: {documentId})", _ai.Prompt);
    }

    /// <summary>
    /// The rule checks read this table by patient id. Chat reading it by document navigation was
    /// the structural difference between the two paths; a row must not be able to drive an alert
    /// the chat context has never seen.
    /// </summary>
    [Fact]
    public async Task IncludesAWarningWhoseDocumentIsNotOtherwiseListed()
    {
        var (patientId, _) = SeedWarningAndMatchingMedication(
            substance: "paracetamol",
            warning: "Avoid liver-toxic medications.");

        _db.Allergies.Add(new Allergy
        {
            PatientId = patientId,
            // Deliberately orphaned: cascade delete should make this impossible, which is exactly
            // why its absence would be invisible.
            DocumentId = Guid.NewGuid(),
            IsDocumentWarning = true,
            Substance = "warfarin",
            RelatesTo = ["warfarin"],
            SourceText = "Stop warfarin before any dental work.",
            Confidence = 80
        });

        await _db.SaveChangesAsync();

        await Service().AskAsync(patientId, "anything to avoid?");

        Assert.Contains("Stop warfarin before any dental work.", _ai.Prompt);
    }

    // ---- fixtures ----

    private GroundedChatService Service() =>
        new(_db, new PromptLibrary(NullLogger<PromptLibrary>.Instance), new SingleService(_ai),
            NullLogger<GroundedChatService>.Instance);

    /// <summary>
    /// The reported defect, on patient_x_year1_1: the page prints "Diagnosis: MALARIA" directly
    /// above four drugs, and "what medicines was I given for malaria?" answered "there is no
    /// mention of any medicines given specifically for malaria". The model was right about what it
    /// was handed — diagnoses were extracted, then dropped at the merge, so the word never reached
    /// the record.
    ///
    /// Asserted on the record text rather than on an answer: what is under test is the context the
    /// pipeline assembles, and the condition has to sit under the same document heading as the
    /// drugs for the model to join them.
    /// </summary>
    [Fact]
    public async Task RecordCarriesADiagnosisAlongsideThatVisitsMedications()
    {
        var (patientId, documentId) = SeedWarningAndMatchingMedication(
            substance: "clarithromycin", warning: "Complete the full course.");

        _db.Diagnoses.Add(new Diagnosis
        {
            PatientId = patientId,
            DocumentId = documentId,
            Text = "Malaria",
            SourceText = "* MALARIA",
            Confidence = 90
        });

        await _db.SaveChangesAsync();

        await Service().AskAsync(patientId, "What medicines was I given for malaria?");

        var record = _ai.Prompt;

        Assert.Contains("Malaria", record, StringComparison.Ordinal);
        Assert.Contains("* MALARIA", record, StringComparison.Ordinal);

        // Under the document heading, and above that document's medications — adjacency is the
        // whole point, not mere presence somewhere in the prompt.
        var heading = record.IndexOf($"## Document id: {documentId}", StringComparison.Ordinal);
        var diagnosis = record.IndexOf("Diagnosis recorded on this document", StringComparison.Ordinal);
        var medication = record.IndexOf("- Medication: clarithromycin", StringComparison.Ordinal);

        Assert.True(heading >= 0 && diagnosis > heading && medication > diagnosis,
            "The diagnosis must sit under its document heading and before that visit's medications.");
    }

    /// <summary>
    /// "Was warfarin prescribed with aspirin?" → "Yes, on August 5, 2019…" → "When?" used to reach
    /// the model with no idea what "when" referred to. The prior turns now travel with the
    /// question, in their own section.
    /// </summary>
    [Fact]
    public async Task SendsPriorTurnsAsConversationContextSeparateFromTheRecord()
    {
        var (patientId, _) = SeedWarningAndMatchingMedication(
            substance: "warfarin", warning: "Monitor INR closely.");

        await _db.SaveChangesAsync();

        await Service().AskAsync(patientId, "When?", [
            new ChatTurn { Question = "Was warfarin prescribed with aspirin?", Answer = "Yes, on August 5, 2019." }
        ]);

        var prompt = _ai.Prompt;

        Assert.Contains("Was warfarin prescribed with aspirin?", prompt, StringComparison.Ordinal);
        Assert.Contains("Yes, on August 5, 2019.", prompt, StringComparison.Ordinal);

        // Fenced as conversation, and after the record — the model must not be able to read a
        // sentence the user typed as something a document says.
        var record = prompt.IndexOf("# The patient's record", StringComparison.Ordinal);
        var conversation = prompt.IndexOf("# Earlier in this conversation", StringComparison.Ordinal);

        Assert.True(record >= 0 && conversation > record,
            "Conversation history must be its own section, after the record.");

        // The grounding rule has to survive the longer prompt, restated where the history sits.
        Assert.Contains("This is not a source.", prompt, StringComparison.Ordinal);
        Assert.Contains("never cite a turn", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// A first question must look exactly as it did before history existed — an empty section, not
    /// an empty heading inviting the model to reason about a conversation that never happened.
    /// </summary>
    [Fact]
    public async Task OmitsTheConversationSectionEntirelyOnTheFirstQuestion()
    {
        var (patientId, _) = SeedWarningAndMatchingMedication(
            substance: "paracetamol", warning: "Avoid acetaminophen.");

        await _db.SaveChangesAsync();

        await Service().AskAsync(patientId, "What am I taking?");

        Assert.DoesNotContain("# Earlier in this conversation", _ai.Prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The record is the large half of this prompt; history competes with it for the same budget.
    /// The client is not trusted to bound what it sends.
    /// </summary>
    [Fact]
    public async Task KeepsOnlyTheMostRecentExchanges()
    {
        var (patientId, _) = SeedWarningAndMatchingMedication(
            substance: "paracetamol", warning: "Avoid acetaminophen.");

        await _db.SaveChangesAsync();

        var history = Enumerable.Range(1, 7)
            .Select(i => new ChatTurn { Question = $"Question number {i}", Answer = $"Answer number {i}" })
            .ToList();

        await Service().AskAsync(patientId, "And now?", history);

        var prompt = _ai.Prompt;

        // Four kept, oldest three dropped.
        Assert.DoesNotContain("Question number 3", prompt, StringComparison.Ordinal);
        Assert.Contains("Question number 4", prompt, StringComparison.Ordinal);
        Assert.Contains("Question number 7", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// A claim that exists only in an earlier answer is not a fact about this person. The stub
    /// model reports a contradiction only from the record, so an assertion planted in the history
    /// must not produce one — and must not become a citation.
    /// </summary>
    [Fact]
    public async Task DoesNotTreatAClaimFromAnEarlierAnswerAsPartOfTheRecord()
    {
        // Seeded without the helper: it pairs a medication with a warning naming the same
        // substance, which is a contradiction on its own. This record deliberately holds none, so
        // the only place a contradiction could come from is the history.
        var (patientId, _) = SeedWarningAndMatchingMedication(
            substance: "paracetamol", warning: "Take after food.");

        await _db.SaveChangesAsync();

        _db.Allergies.RemoveRange(await _db.Allergies.ToListAsync());
        await _db.SaveChangesAsync();

        var answer = await Service().AskAsync(patientId, "So am I allergic to penicillin?", [
            new ChatTurn
            {
                Question = "Anything I should avoid?",
                Answer = "You are allergic to penicillin and were given amoxicillin."
            }
        ]);

        // Neither substance is anywhere in this patient's documents.
        Assert.False(answer.FoundInDocuments);
        Assert.Empty(answer.Citations);
    }

    private (Guid PatientId, Guid DocumentId) SeedWarningAndMatchingMedication(
        string substance, string warning, bool isDocumentWarning = true)
    {
        var patientId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        _db.Patients.Add(new Patient { Id = patientId, DisplayName = "Test" });

        _db.Documents.Add(new Document
        {
            Id = documentId,
            PatientId = patientId,
            OriginalFileName = $"{documentId}.jpg",
            ContentType = "image/jpeg",
            StoragePath = $"{patientId}/{documentId}.jpg",
            Sha256 = documentId.ToString("N") + documentId.ToString("N"),
            DocumentType = "prescription",
            Status = DocumentStatus.Extracted,
            OverallConfidence = 90
        });

        _db.Medications.Add(new Medication
        {
            PatientId = patientId,
            DocumentId = documentId,
            GenericName = substance,
            StrengthValue = 500,
            StrengthUnit = "mg",
            Confidence = 90
        });

        _db.Allergies.Add(new Allergy
        {
            PatientId = patientId,
            DocumentId = documentId,
            IsDocumentWarning = isDocumentWarning,
            Substance = substance,
            SubstanceGeneric = substance,
            RelatesTo = [substance],
            SourceText = warning,
            Confidence = 90
        });

        return (patientId, documentId);
    }

    /// <summary>
    /// Stands in for the model. Reads the record it was given and reports a contradiction only when
    /// the record contains both a medication and an instruction not to take that same substance —
    /// so the assertions are about the context the pipeline builds, never about model behaviour,
    /// which cannot be pinned down offline.
    /// </summary>
    private sealed partial class RecordReadingAiClient : IAiClient
    {
        public string Prompt { get; private set; } = "(never called)";

        public Task<AiCompletion> CompleteAsync(
            string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
        {
            Prompt = systemPrompt;

            var medications = MedicationLine().Matches(systemPrompt)
                .Select(m => m.Groups[1].Value.Trim())
                .ToList();

            Guid document = default;
            string? conflicting = null;

            // Walk the record in order so the substance can be attributed to the document section
            // it was found under.
            foreach (var raw in systemPrompt.Split('\n'))
            {
                var line = raw.TrimEnd();

                var header = DocumentHeader().Match(line);
                if (header.Success && Guid.TryParse(header.Groups[1].Value, out var id))
                {
                    document = id;
                    continue;
                }

                var avoid = AvoidLine().Match(line);
                if (!avoid.Success) continue;

                conflicting = avoid.Groups[1].Value
                    .Split(',')
                    .Select(s => s.Trim())
                    .FirstOrDefault(s => medications.Any(m => string.Equals(m, s, StringComparison.OrdinalIgnoreCase)));

                if (conflicting is not null) break;
            }

            var found = conflicting is not null && document != default;

            var answer = found
                ? $"{conflicting} was prescribed, and this record says not to take it."
                : "I could not find that in your uploaded documents.";

            return Task.FromResult(new AiCompletion
            {
                Content = $$"""
                {
                  "answerEn": "{{answer}}",
                  "answerTa": "…",
                  "citations": [{{(found ? $"\"{document}\"" : string.Empty)}}],
                  "confidence": {{(found ? 90 : 10)}},
                  "consultProfessional": {{(found ? "true" : "false")}},
                  "foundInDocuments": {{(found ? "true" : "false")}}
                }
                """,
                Model = "stub",
                LatencyMs = 1
            });
        }

        public Task<AiCompletion> CompleteWithImagesAsync(
            string systemPrompt, IReadOnlyList<byte[]> images, string imageContentType,
            string? model = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        [GeneratedRegex(@"^- Medication: ([^,\r\n]+)", RegexOptions.Multiline)]
        private static partial Regex MedicationLine();

        [GeneratedRegex(@"^## Document id: (\S+)")]
        private static partial Regex DocumentHeader();

        [GeneratedRegex(@"do not take ([^:\r\n]+?)(?::|$)")]
        private static partial Regex AvoidLine();
    }

    private sealed class SingleService(IAiClient ai) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IAiClient) ? ai : null;
    }
}
