using System.Text;
using System.Text.Json.Serialization;
using MediTrail.Api.AiPipeline.Extraction;
using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.AiPipeline.Verification;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediTrail.Api.AiPipeline.CrossCheck;

/// <summary>
/// Stages 4 and 5 of the pipeline (§11.1): drug–drug interactions proposed by the LLM, then
/// corroborated against openFDA.
///
/// The division of labour is the point (Principle 4): the model proposes, openFDA corroborates,
/// and disagreement is surfaced rather than hidden. A finding openFDA cannot confirm is **kept**
/// and badged unverified — absence of confirmation is not evidence of safety (FR-5.7).
/// </summary>
public interface IInteractionCrossChecker
{
    Task<IReadOnlyList<Alert>> CheckAsync(Guid patientId, CancellationToken ct = default);
}

public sealed class InteractionCrossChecker(
    MediTrailDbContext db,
    IAiClient ai,
    IPromptLibrary prompts,
    IOpenFdaClient openFda,
    ILogger<InteractionCrossChecker> logger) : IInteractionCrossChecker
{
    public async Task<IReadOnlyList<Alert>> CheckAsync(Guid patientId, CancellationToken ct = default)
    {
        var medications = await db.Medications
            .AsNoTracking()
            .Where(m => m.PatientId == patientId && m.GenericName != null)
            .ToListAsync(ct);

        var byGeneric = medications
            .GroupBy(m => m.GenericName!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // One medicine cannot interact with itself, and asking the model anyway wastes budget.
        if (byGeneric.Count < 2)
        {
            logger.LogInformation("Skipping interaction check for {PatientId}: fewer than two distinct medications",
                patientId);
            return [];
        }

        InteractionResponse? response;
        try
        {
            var prompt = prompts.Get("crosscheck", new Dictionary<string, string>
            {
                ["MEDICATIONS"] = FormatMedications(byGeneric)
            });

            var completion = await ai.CompleteAsync(prompt, "Review this medication history.", ct: ct);

            if (!JsonResponseReader.TryRead<InteractionResponse>(completion.Content, out response, out var error))
            {
                // A malformed cross-check must not take the whole analysis down; the deterministic
                // findings already computed are still valid and still shown.
                logger.LogError("Cross-check returned unusable JSON for {PatientId}: {Error}", patientId, error);
                return [];
            }
        }
        catch (AiClientException ex)
        {
            logger.LogError(ex, "Cross-check unavailable for {PatientId}", patientId);
            return [];
        }

        var alerts = new List<Alert>();

        foreach (var finding in response!.Findings)
        {
            if (finding.Confidence < 60) continue;

            // Grounding: the model may only name medications that are actually in the record
            // (§11.5). Anything else is dropped, not repaired.
            if (!byGeneric.TryGetValue(Key(finding.GenericA), out var a) ||
                !byGeneric.TryGetValue(Key(finding.GenericB), out var b))
            {
                // Debug, not Warning: the drug names are this patient's medication history, and
                // a level that reaches production logs would put them there on every analysis.
                logger.LogDebug(
                    "Dropped interaction {A} + {B} for {PatientId}: not in the patient's record",
                    finding.GenericA, finding.GenericB, patientId);
                continue;
            }

            if (Key(finding.GenericA) == Key(finding.GenericB)) continue;

            // Temporal gate: two drugs the patient was never taking at the same time cannot
            // interact. Deterministic, so the model cannot talk its way past it (Principle 2).
            var concurrency = MedicationWindowCalculator.Compare(a, b);

            if (concurrency == Concurrency.NotConcurrent)
            {
                logger.LogDebug(
                    "Dropped interaction {A} + {B} for {PatientId}: prescriptions do not overlap in time",
                    finding.GenericA, finding.GenericB, patientId);
                continue;
            }

            var verification = await VerifyAsync(Key(finding.GenericA), Key(finding.GenericB), ct);

            var severity = ParseSeverity(finding.Severity);
            var confidence = AdjustConfidence(finding.Confidence, verification, a, b);

            alerts.Add(new Alert
            {
                PatientId = patientId,
                Type = AlertType.DrugInteraction,
                Severity = severity,
                Title = $"{Display(finding.GenericA)} and {Display(finding.GenericB)} may interact",
                InvolvedGenerics = [Key(finding.GenericA), Key(finding.GenericB)],
                ExplanationEn = WithCaveat(finding.ExplanationEn, concurrency,
                    MedicationWindowCalculator.DateUnknownCaveatEn),
                ExplanationTa = WithCaveat(finding.ExplanationTa, concurrency,
                    MedicationWindowCalculator.DateUnknownCaveatTa),
                SuggestedActionEn = finding.SuggestedActionEn,
                SuggestedActionTa = finding.SuggestedActionTa,
                Confidence = confidence,
                // Red severity or low confidence always carries the consult banner (§11.4).
                RequiresProfessionalConsult = severity == AlertSeverity.Red || confidence < 50,
                VerificationStatus = verification.Status,
                VerificationExcerpt = verification.Excerpt,
                VerificationSource = verification.Source,
                EvidenceDocumentIds = a.Concat(b).Select(m => m.DocumentId).Distinct().ToList(),
                DetectedBy = "llm"
            });
        }

        logger.LogInformation("Cross-check raised {Count} interaction alert(s) for {PatientId}",
            alerts.Count, patientId);

        return alerts;
    }

    /// <summary>
    /// Appends the "dates unknown" caveat when the pair only survived the gate because a document
    /// had no readable date. An explanation the model did not write is left alone — half a
    /// sentence about a medication risk is worse than none.
    /// </summary>
    private static string? WithCaveat(string? explanation, Concurrency concurrency, string caveat)
    {
        if (concurrency != Concurrency.DateUnknown) return explanation;
        if (string.IsNullOrWhiteSpace(explanation)) return explanation;

        return $"{explanation.TrimEnd()} {caveat}";
    }

    /// <summary>
    /// Checks both labels — an interaction is often documented on only one side. A failed lookup
    /// yields <see cref="VerificationStatus.Unverified"/>, never removal of the finding.
    /// </summary>
    private async Task<(VerificationStatus Status, string? Excerpt, string? Source)> VerifyAsync(
        string a, string b, CancellationToken ct)
    {
        var forward = await openFda.VerifyInteractionAsync(a, b, ct);
        if (forward.Confirmed) return (VerificationStatus.Confirmed, forward.Excerpt, forward.Source);

        var reverse = await openFda.VerifyInteractionAsync(b, a, ct);
        if (reverse.Confirmed) return (VerificationStatus.Confirmed, reverse.Excerpt, reverse.Source);

        if (forward.LookupFailed && reverse.LookupFailed)
        {
            return (VerificationStatus.Unverified, null, null);
        }

        // Both labels resolved and neither mentions the other. Worth saying plainly.
        return (VerificationStatus.NotFound, null, null);
    }

    /// <summary>
    /// Composed confidence (§11.4): the model's self-assessment, lowered by how well the
    /// underlying extractions read, then adjusted by whether an independent source agreed.
    /// </summary>
    private static int AdjustConfidence(
        int modelConfidence,
        (VerificationStatus Status, string? Excerpt, string? Source) verification,
        List<Medication> a,
        List<Medication> b)
    {
        var extractionFloor = a.Concat(b)
            .Select(m => m.Confidence)
            .Where(c => c is not null)
            .Select(c => c!.Value)
            .DefaultIfEmpty(70)
            .Min();

        // A finding can never be more trustworthy than the reading it rests on.
        var score = Math.Min(modelConfidence, extractionFloor);

        score += verification.Status switch
        {
            VerificationStatus.Confirmed => 15,
            // Not a penalty for being wrong — many real interactions are simply not on a US label,
            // and openFDA does not cover every drug in this dataset.
            VerificationStatus.NotFound => -5,
            _ => 0
        };

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// The model sees generic names, strengths and dates — not the raw documents. Grounding it on
    /// the structured record is what keeps it from reasoning about anything else (§11.3).
    /// </summary>
    private static string FormatMedications(Dictionary<string, List<Medication>> byGeneric)
    {
        var builder = new StringBuilder();

        foreach (var (generic, items) in byGeneric.OrderBy(kv => kv.Key))
        {
            var strengths = items
                .Where(m => m.StrengthValue is not null)
                .Select(m => $"{m.StrengthValue}{m.StrengthUnit}")
                .Distinct()
                .ToList();

            // The active window, not just the prescribing month: the model is told which medicines
            // could have been in use together, so it is less likely to propose a pair the temporal
            // gate would only throw away.
            var periods = items
                .Select(MedicationWindowCalculator.Describe)
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            builder.Append("- ").Append(generic);

            if (items[0].BrandName is { } brand) builder.Append($" (as {brand})");
            if (strengths.Count > 0) builder.Append($", {string.Join(" / ", strengths)}");
            if (periods.Count > 0) builder.Append($", active {string.Join("; ", periods)}");

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Key(string name) => name.Trim().ToLowerInvariant();

    private static string Display(string generic) =>
        generic.Length == 0 ? generic : char.ToUpperInvariant(generic[0]) + generic.Trim()[1..].ToLowerInvariant();

    private static AlertSeverity ParseSeverity(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "red" => AlertSeverity.Red,
        "amber" => AlertSeverity.Amber,
        _ => AlertSeverity.Info
    };

    private sealed record InteractionResponse
    {
        [JsonPropertyName("findings")]
        public IReadOnlyList<InteractionFinding> Findings { get; init; } = [];
    }

    private sealed record InteractionFinding
    {
        [JsonPropertyName("genericA")] public string GenericA { get; init; } = string.Empty;
        [JsonPropertyName("genericB")] public string GenericB { get; init; } = string.Empty;
        [JsonPropertyName("severity")] public string? Severity { get; init; }
        [JsonPropertyName("explanationEn")] public string? ExplanationEn { get; init; }
        [JsonPropertyName("explanationTa")] public string? ExplanationTa { get; init; }
        [JsonPropertyName("suggestedActionEn")] public string? SuggestedActionEn { get; init; }
        [JsonPropertyName("suggestedActionTa")] public string? SuggestedActionTa { get; init; }
        [JsonPropertyName("confidence")] public int Confidence { get; init; }
    }
}
