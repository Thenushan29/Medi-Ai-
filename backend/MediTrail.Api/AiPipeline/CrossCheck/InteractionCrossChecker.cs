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
            var groundedA = Grounded(byGeneric, finding.GenericA);
            var groundedB = Grounded(byGeneric, finding.GenericB);

            if (groundedA.Count == 0 || groundedB.Count == 0)
            {
                // Debug, not Warning: the drug names are this patient's medication history, and
                // a level that reaches production logs would put them there on every analysis.
                logger.LogDebug(
                    "Dropped interaction {A} + {B} for {PatientId}: not in the patient's record",
                    finding.GenericA, finding.GenericB, patientId);
                continue;
            }

            // Both sides landing on the same rows is one product, not two drugs: a model naming
            // aspirin and codeine separately is describing a single combination tablet, and
            // "Aspirin and Codeine may interact" about one tablet is not a finding. This also
            // covers the case where the model names the identical generic twice.
            if (groundedA.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(groundedB.Keys))
            {
                continue;
            }

            var a = groundedA.Values.SelectMany(rows => rows).ToList();
            var b = groundedB.Values.SelectMany(rows => rows).ToList();

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
                // The generics as they sit on the record, not as the model named them. When the
                // patient's row is a combination product this names the product, which is what
                // the medications table and the evidence viewer key off — and it tells the reader
                // which item on their own record carries the drug the title is about.
                InvolvedGenerics = [.. groundedA.Keys.Concat(groundedB.Keys).Distinct()],
                ExplanationEn = WithCaveat(finding.ExplanationEn, concurrency,
                    MedicationWindowCalculator.DateUnknownCaveatEn),
                ExplanationTa = WithCaveat(finding.ExplanationTa, concurrency,
                    MedicationWindowCalculator.DateUnknownCaveatTa),
                SuggestedActionEn = finding.SuggestedActionEn,
                SuggestedActionTa = finding.SuggestedActionTa,
                Confidence = confidence,
                // Every interaction above the informational tier carries the consult banner, not
                // only the red ones. The rules require flagging any high-risk *or* low-confidence
                // output (FR-7.6, §11.4), and an amber interaction is a claim that two medicines
                // the patient is taking together may harm them — there is no reading of that which
                // the patient should act on alone. Measured on the evaluation set, every one of
                // sixteen interaction alerts came back amber and none carried the banner.
                //
                // Info stays unflagged unless the confidence is poor: a banner on every minor
                // finding is a banner nobody reads by the time a red one appears.
                RequiresProfessionalConsult = severity != AlertSeverity.Info || confidence < 50,
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

    /// <summary>
    /// The medication rows a proposed generic is grounded in, keyed by the generic as it sits on
    /// the record.
    ///
    /// Matching is component-wise rather than whole-string, so a combination product on the record
    /// satisfies a lookup for any one of its ingredients. On the evaluation set the model proposed
    /// warfarin + aspirin, the record held <c>aspirin/codeine</c>, the exact-key lookup missed, and
    /// the strongest interaction in the dataset was dropped in silence (traps.md X1).
    ///
    /// It holds in the other direction too: a model naming the combination is grounded by a record
    /// holding the single ingredient. An empty result still means ungrounded, and ungrounded
    /// findings are still dropped — this widens what counts as present, not what counts as true.
    /// </summary>
    private static Dictionary<string, List<Medication>> Grounded(
        Dictionary<string, List<Medication>> byGeneric, string proposed) =>
        byGeneric
            .Where(entry => DrugNameNormalizer.SharesComponent(entry.Key, proposed))
            .ToDictionary(entry => entry.Key, entry => entry.Value);

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
