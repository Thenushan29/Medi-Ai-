using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Data.Entities;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

/// <summary>
/// Canonical specialty codes use British OSM spellings. Disease-class names from RxClass
/// are drug-class information, never a statement about the patient.
/// </summary>
public static class SpecialtyCatalog
{
    public static readonly IReadOnlyList<SpecialtyOptionDto> All =
    [
        new() { Code = "general_practice", Label = "General practice" },
        new() { Code = "cardiology", Label = "Cardiology" },
        new() { Code = "endocrinology", Label = "Endocrinology" },
        new() { Code = "nephrology", Label = "Nephrology" },
        new() { Code = "allergy_immunology", Label = "Allergy / immunology" },
        new() { Code = "gynaecology", Label = "Gynaecology" },
        new() { Code = "orthopaedics", Label = "Orthopaedics" },
        new() { Code = "paediatrics", Label = "Paediatrics" },
        new() { Code = "neurology", Label = "Neurology" }
    ];

    public static string LabelFor(string code) =>
        All.FirstOrDefault(s => s.Code == code)?.Label ?? code;
}

public static class SpecialtyMaps
{
    public const string RxNormMissReason = "This medication isn't in the NLM RxNorm vocabulary";

    public const string RxClassUnreachableReason =
        "We couldn't look up this medication in NLM RxClass just now, so we suggest general practice.";

    public const string NoSignalReason =
        "No specialty-specific signal was available, so we suggest general practice.";

    public const string RxClassDiseaseReason =
        "Derived from the medications in this alert via NLM RxClass (MED-RT, may_treat).";

    public const string RxClassAtcReason =
        "Derived from the medication's pharmacologic class via NLM RxClass (ATC).";

    /// <summary>MEDRT may_treat DISEASE class names → specialty. Exact, case-insensitive.</summary>
    public static readonly Dictionary<string, string> DiseaseClassToSpecialty = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Thromboembolism"] = "cardiology",
        ["Pulmonary Embolism"] = "cardiology",
        ["Atrial Fibrillation"] = "cardiology",
        ["Thrombophlebitis"] = "cardiology",
        ["Myocardial Infarction"] = "cardiology",
        ["Heart Failure"] = "cardiology",
        ["Angina Pectoris"] = "cardiology",
        ["Hypertension"] = "cardiology",
        ["Coronary Artery Disease"] = "cardiology",
        ["Stroke"] = "neurology",
        ["Diabetes Mellitus"] = "endocrinology",
        ["Diabetes Mellitus, Type 2"] = "endocrinology",
        ["Diabetes Mellitus, Type 1"] = "endocrinology",
        ["Hypothyroidism"] = "endocrinology",
        ["Hyperthyroidism"] = "endocrinology",
        ["Kidney Failure, Chronic"] = "nephrology",
        ["Renal Insufficiency"] = "nephrology",
        ["Glomerulonephritis"] = "nephrology",
        ["Nephritis"] = "nephrology",
        ["Anaphylaxis"] = "allergy_immunology",
        ["Rhinitis, Allergic"] = "allergy_immunology",
        ["Asthma"] = "allergy_immunology",
        ["Endometriosis"] = "gynaecology",
        ["Osteoarthritis"] = "orthopaedics",
        ["Arthritis, Rheumatoid"] = "orthopaedics"
    };

    public static readonly Dictionary<string, string> LabKeyToSpecialty = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hba1c"] = "endocrinology",
        ["fasting glucose"] = "endocrinology",
        ["random glucose"] = "endocrinology",
        ["glucose"] = "endocrinology",
        ["tsh"] = "endocrinology",
        ["creatinine"] = "nephrology",
        ["egfr"] = "nephrology",
        ["gfr"] = "nephrology",
        ["bun"] = "nephrology",
        ["urea"] = "nephrology"
    };

    public static bool TryMapDiseaseClass(string className, out string specialty)
    {
        if (DiseaseClassToSpecialty.TryGetValue(className, out specialty!))
            return true;

        var lower = className.ToLowerInvariant();
        if (lower.Contains("thrombo") || lower.Contains("atrial fibril") || lower.Contains("pulmonary embol")
            || lower.Contains("myocardial") || lower.Contains("heart failure") || lower.Contains("angina"))
        {
            specialty = "cardiology";
            return true;
        }

        if (lower.Contains("diabet") || lower.Contains("thyroid"))
        {
            specialty = "endocrinology";
            return true;
        }

        if (lower.Contains("kidney") || lower.Contains("renal") || lower.Contains("nephro"))
        {
            specialty = "nephrology";
            return true;
        }

        if (lower.Contains("allerg") || lower.Contains("anaphyl"))
        {
            specialty = "allergy_immunology";
            return true;
        }

        specialty = "";
        return false;
    }

    public static bool TryMapAtc(string classId, out string specialty)
    {
        var id = classId.Trim().ToUpperInvariant();
        if (id.StartsWith("C", StringComparison.Ordinal) || id.StartsWith("B01", StringComparison.Ordinal))
        {
            specialty = "cardiology";
            return true;
        }

        if (id.StartsWith("A10", StringComparison.Ordinal))
        {
            specialty = "endocrinology";
            return true;
        }

        if (id.StartsWith("G03", StringComparison.Ordinal))
        {
            specialty = "gynaecology";
            return true;
        }

        if (id.StartsWith("M01", StringComparison.Ordinal) || id.StartsWith("M02", StringComparison.Ordinal))
        {
            specialty = "orthopaedics";
            return true;
        }

        if (id.StartsWith("R03", StringComparison.Ordinal) || id.StartsWith("R06", StringComparison.Ordinal))
        {
            specialty = "allergy_immunology";
            return true;
        }

        if (id.StartsWith("N03", StringComparison.Ordinal) || id.StartsWith("N04", StringComparison.Ordinal))
        {
            specialty = "neurology";
            return true;
        }

        specialty = "";
        return false;
    }

    public static bool TryMapLabKey(string labKey, out string specialty) =>
        LabKeyToSpecialty.TryGetValue(labKey, out specialty!);

    public static bool AllowsPharmacy(AlertType? type) =>
        type is AlertType.DrugInteraction or AlertType.DuplicatePrescription or AlertType.DosageConflict;
}
