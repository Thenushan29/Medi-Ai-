using MediTrail.Api.AiPipeline.Normalization;
using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Data.Entities;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

public sealed record SpecialtyContext
{
    public AlertType? AlertType { get; init; }
    public IReadOnlyList<string> DrugNames { get; init; } = [];
    public IReadOnlyList<string> LabTestKeys { get; init; } = [];
    public string? Override { get; init; }
}

public sealed record SpecialtyResolution
{
    public required string Code { get; init; }
    public required string Label { get; init; }
    public required string ResolvedBy { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<SpecialtyEvidenceDto> Evidence { get; init; } = [];
    public bool AllowPharmacy { get; init; }

    public SpecialtyResolutionDto ToDto() => new()
    {
        Code = Code,
        Label = Label,
        ResolvedBy = ResolvedBy,
        Reason = Reason,
        Evidence = Evidence
    };
}

public interface ISpecialtyResolver
{
    Task<SpecialtyResolution> ResolveAsync(SpecialtyContext context, CancellationToken ct = default);
}

/// <summary>
/// Deterministic specialty ladder. The LLM never picks the specialty. First matching rung wins.
/// </summary>
public sealed class SpecialtyResolver(IRxClassClient rxClass) : ISpecialtyResolver
{
    public async Task<SpecialtyResolution> ResolveAsync(
        SpecialtyContext context, CancellationToken ct = default)
    {
        var allowPharmacy = SpecialtyMaps.AllowsPharmacy(context.AlertType);

        if (!string.IsNullOrWhiteSpace(context.Override))
        {
            var code = context.Override.Trim();
            return Finish(code, "user_override", "Chosen from the specialty list.", [], allowPharmacy);
        }

        // Rung 1 — AlertType rules.
        if (TryAlertType(context, allowPharmacy, out var rung1))
            return rung1;

        // Rung 2 — RxClass MEDRT may_treat DISEASE classes.
        var (rung2, rxFailed, rxAttempted) = await TryRxClassDiseaseAsync(context, allowPharmacy, ct);
        if (rung2 is not null) return rung2;

        // Rung 3 — ATC / EPC pharmacologic class.
        var rung3 = await TryAtcAsync(context, allowPharmacy, ct);
        if (rung3 is not null) return rung3;

        // Rung 4 — lab test grouping keys.
        if (TryLabs(context, allowPharmacy, out var rung4))
            return rung4;

        // Rung 6 — honest fallback.
        var reason = rxFailed
            ? SpecialtyMaps.RxClassUnreachableReason
            : rxAttempted
                ? SpecialtyMaps.RxNormMissReason
                : SpecialtyMaps.NoSignalReason;

        return Finish("general_practice", "fallback", reason, [], allowPharmacy);
    }

    private static bool TryAlertType(
        SpecialtyContext context, bool allowPharmacy, out SpecialtyResolution resolution)
    {
        resolution = null!;
        if (context.AlertType is null) return false;

        switch (context.AlertType)
        {
            case AlertType.AllergyConflict:
                resolution = Finish(
                    "allergy_immunology",
                    "alert_type",
                    "This alert is an allergy conflict, so we suggest an allergy / immunology clinic.",
                    [RungEvidence("alert_type", "Allergy conflict")],
                    allowPharmacy);
                return true;

            case AlertType.LowExtractionConfidence:
                resolution = Finish(
                    "general_practice",
                    "alert_type",
                    "Extraction confidence was low, so we suggest general practice.",
                    [RungEvidence("alert_type", "Low extraction confidence")],
                    allowPharmacy);
                return true;

            case AlertType.UnresolvedMedication:
                resolution = Finish(
                    "general_practice",
                    "alert_type",
                    "The medication name could not be resolved, so we suggest general practice.",
                    [RungEvidence("alert_type", "Unresolved medication")],
                    allowPharmacy);
                return true;

            case AlertType.LabDrift:
            case AlertType.LabOutOfRange:
                if (TryLabs(context, allowPharmacy, out var lab, resolvedBy: "alert_type"))
                {
                    resolution = lab;
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private async Task<(SpecialtyResolution? Resolution, bool LookupFailed, bool Attempted)> TryRxClassDiseaseAsync(
        SpecialtyContext context, bool allowPharmacy, CancellationToken ct)
    {
        var attempted = false;
        var failed = false;

        foreach (var raw in context.DrugNames)
        {
            var query = RxClassClient.ToQueryName(raw);
            if (query is null) continue;

            attempted = true;
            var lookup = await rxClass.MayTreatAsync(raw, ct);
            if (lookup.LookupFailed)
            {
                failed = true;
                continue;
            }

            var mapped = lookup.Hits
                .Where(h => string.Equals(h.ClassType, "DISEASE", StringComparison.OrdinalIgnoreCase)
                            || h.ClassType.Length == 0)
                .Select(h => (Hit: h, Mapped: SpecialtyMaps.TryMapDiseaseClass(h.ClassName, out var code) ? code : null))
                .Where(x => x.Mapped is not null)
                .ToList();

            if (mapped.Count == 0) continue;

            var code = mapped[0].Mapped!;
            var evidence = mapped
                .Where(x => x.Mapped == code)
                .Select(x => new SpecialtyEvidenceDto
                {
                    Type = "rxclass_class",
                    Label = x.Hit.ClassName,
                    Source = x.Hit.RelaSource,
                    SourceId = x.Hit.ClassId,
                    SourceUrl = RxClassClient.ClassUrl(x.Hit.RelaSource, x.Hit.ClassId)
                })
                .ToList();

            return (Finish(code, "rxclass_disease", SpecialtyMaps.RxClassDiseaseReason, evidence, allowPharmacy),
                false, true);
        }

        return (null, failed, attempted);
    }

    private async Task<SpecialtyResolution?> TryAtcAsync(
        SpecialtyContext context, bool allowPharmacy, CancellationToken ct)
    {
        foreach (var raw in context.DrugNames)
        {
            if (RxClassClient.ToQueryName(raw) is null) continue;

            var lookup = await rxClass.AtcClassesAsync(raw, ct);
            if (lookup.LookupFailed || lookup.Hits.Count == 0) continue;

            foreach (var hit in lookup.Hits)
            {
                if (!SpecialtyMaps.TryMapAtc(hit.ClassId, out var code)) continue;

                return Finish(
                    code,
                    "rxclass_atc",
                    SpecialtyMaps.RxClassAtcReason,
                    [
                        new SpecialtyEvidenceDto
                        {
                            Type = "rxclass_class",
                            Label = hit.ClassName,
                            Source = hit.RelaSource,
                            SourceId = hit.ClassId,
                            SourceUrl = RxClassClient.ClassUrl(hit.RelaSource, hit.ClassId)
                        }
                    ],
                    allowPharmacy);
            }
        }

        return null;
    }

    private static bool TryLabs(
        SpecialtyContext context,
        bool allowPharmacy,
        out SpecialtyResolution resolution,
        string resolvedBy = "lab_test")
    {
        resolution = null!;
        foreach (var raw in context.LabTestKeys)
        {
            var key = LabTestNormalizer.Standardize(raw) ?? raw.Trim().ToLowerInvariant();
            if (!SpecialtyMaps.TryMapLabKey(key, out var code)) continue;

            var reason = resolvedBy == "alert_type"
                ? $"This alert refers to lab results grouped as {key}, so we suggest {SpecialtyCatalog.LabelFor(code)}."
                : $"Lab results grouped as {key} point to {SpecialtyCatalog.LabelFor(code)}.";

            resolution = Finish(
                code,
                resolvedBy,
                reason,
                [RungEvidence("lab_test", key, "lab_test_normalizer")],
                allowPharmacy);
            return true;
        }

        return false;
    }

    private static SpecialtyResolution Finish(
        string code,
        string resolvedBy,
        string reason,
        IReadOnlyList<SpecialtyEvidenceDto> evidence,
        bool allowPharmacy) =>
        new()
        {
            Code = code,
            Label = SpecialtyCatalog.LabelFor(code),
            ResolvedBy = resolvedBy,
            Reason = reason,
            Evidence = evidence,
            AllowPharmacy = allowPharmacy
        };

    private static SpecialtyEvidenceDto RungEvidence(string type, string label, string? source = "alert_type") =>
        new()
        {
            Type = type,
            Label = label,
            Source = source
        };
}
