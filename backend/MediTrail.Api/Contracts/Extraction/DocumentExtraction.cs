using System.Text.Json.Serialization;

namespace MediTrail.Api.Contracts.Extraction;

/// <summary>
/// The canonical extraction schema (PRD §12.1). Every document type — prescription, lab report,
/// discharge summary, doctor's note — is extracted into this one shape. Sections that do not apply
/// return empty arrays, never a different structure.
///
/// Every value is nullable by design: FR-3.6 requires the model to return null rather than guess.
/// A null here is a correct answer, not a failure.
/// </summary>
public sealed record DocumentExtraction
{
    [JsonPropertyName("documentType")]
    public string? DocumentType { get; init; }

    /// <summary>ISO-8601 (yyyy-MM-dd). Null when the printed date is ambiguous (FR-4.1).</summary>
    [JsonPropertyName("documentDate")]
    public string? DocumentDate { get; init; }

    [JsonPropertyName("documentDateConfidence")]
    public int? DocumentDateConfidence { get; init; }

    [JsonPropertyName("provider")]
    public ProviderInfo? Provider { get; init; }

    [JsonPropertyName("patient")]
    public PatientInfo? Patient { get; init; }

    [JsonPropertyName("diagnoses")]
    public IReadOnlyList<ExtractedDiagnosis> Diagnoses { get; init; } = [];

    [JsonPropertyName("medications")]
    public IReadOnlyList<ExtractedMedication> Medications { get; init; } = [];

    [JsonPropertyName("labResults")]
    public IReadOnlyList<ExtractedLabResult> LabResults { get; init; } = [];

    [JsonPropertyName("allergies")]
    public IReadOnlyList<ExtractedAllergy> Allergies { get; init; } = [];

    /// <summary>
    /// Warnings printed on the document itself, with the substances they reference.
    /// Required to detect same-document contradictions (FR-5.5) — e.g. a prescription listing
    /// Paracetamol whose own advice section says "avoid liver-toxic medications (e.g. acetaminophen)".
    /// </summary>
    [JsonPropertyName("warningsInDocument")]
    public IReadOnlyList<ExtractedWarning> WarningsInDocument { get; init; } = [];

    [JsonPropertyName("clinicalNotes")]
    public string? ClinicalNotes { get; init; }

    [JsonPropertyName("followUpDate")]
    public string? FollowUpDate { get; init; }

    /// <summary>0–100. The model's own assessment of how well it could read this document.</summary>
    [JsonPropertyName("overallConfidence")]
    public int? OverallConfidence { get; init; }

    /// <summary>Why legibility was degraded, if it was — blur, glare, skew, handwriting.</summary>
    [JsonPropertyName("legibilityNotes")]
    public string? LegibilityNotes { get; init; }

    /// <summary>Regions the model could not read at all. Reported, never filled in (US-7).</summary>
    [JsonPropertyName("unreadableSections")]
    public IReadOnlyList<string> UnreadableSections { get; init; } = [];
}

public sealed record ProviderInfo
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("facility")] public string? Facility { get; init; }
    [JsonPropertyName("specialty")] public string? Specialty { get; init; }
    [JsonPropertyName("confidence")] public int? Confidence { get; init; }
}

public sealed record PatientInfo
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("age")] public string? Age { get; init; }
    [JsonPropertyName("sex")] public string? Sex { get; init; }
    [JsonPropertyName("confidence")] public int? Confidence { get; init; }
}

public sealed record ExtractedDiagnosis
{
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("sourceText")] public string? SourceText { get; init; }
    [JsonPropertyName("confidence")] public int? Confidence { get; init; }
}

public sealed record ExtractedMedication
{
    [JsonPropertyName("brandName")] public string? BrandName { get; init; }

    /// <summary>
    /// Active ingredient, lowercase. The join key for every cross-check in the pipeline.
    /// Null unless the brand→generic mapping is confident (PRD §11.3).
    /// </summary>
    [JsonPropertyName("genericName")] public string? GenericName { get; init; }

    [JsonPropertyName("strengthValue")] public decimal? StrengthValue { get; init; }
    [JsonPropertyName("strengthUnit")] public string? StrengthUnit { get; init; }
    [JsonPropertyName("dose")] public string? Dose { get; init; }

    /// <summary>Frequency as printed — "1 Morning, 1 Night", "TDS", "bd".</summary>
    [JsonPropertyName("frequency")] public string? Frequency { get; init; }

    /// <summary>Frequency resolved to doses per day, for numeric comparison in dosage-conflict checks.</summary>
    [JsonPropertyName("frequencyPerDay")] public decimal? FrequencyPerDay { get; init; }

    [JsonPropertyName("route")] public string? Route { get; init; }
    [JsonPropertyName("durationDays")] public int? DurationDays { get; init; }
    [JsonPropertyName("instructions")] public string? Instructions { get; init; }

    /// <summary>The exact printed text this row was read from. Shown beside the normalized value (FR-4.6).</summary>
    [JsonPropertyName("sourceText")] public string? SourceText { get; init; }

    [JsonPropertyName("confidence")] public int? Confidence { get; init; }
}

public sealed record ExtractedLabResult
{
    [JsonPropertyName("testName")] public string? TestName { get; init; }

    /// <summary>Standardized grouping key so the same test taken at different labs lines up on one chart.</summary>
    [JsonPropertyName("testNameStandard")] public string? TestNameStandard { get; init; }

    [JsonPropertyName("valueNumeric")] public decimal? ValueNumeric { get; init; }

    /// <summary>For non-numeric results — "Positive", "Trace", "Not detected".</summary>
    [JsonPropertyName("valueText")] public string? ValueText { get; init; }

    [JsonPropertyName("unit")] public string? Unit { get; init; }
    [JsonPropertyName("normalMin")] public decimal? NormalMin { get; init; }
    [JsonPropertyName("normalMax")] public decimal? NormalMax { get; init; }

    /// <summary>Reference range exactly as printed, when it will not parse into min/max.</summary>
    [JsonPropertyName("normalRangeText")] public string? NormalRangeText { get; init; }

    [JsonPropertyName("testDate")] public string? TestDate { get; init; }
    [JsonPropertyName("sourceText")] public string? SourceText { get; init; }
    [JsonPropertyName("confidence")] public int? Confidence { get; init; }
}

public sealed record ExtractedAllergy
{
    [JsonPropertyName("substance")] public string? Substance { get; init; }
    [JsonPropertyName("substanceGeneric")] public string? SubstanceGeneric { get; init; }
    [JsonPropertyName("reaction")] public string? Reaction { get; init; }
    [JsonPropertyName("severity")] public string? Severity { get; init; }
    [JsonPropertyName("sourceText")] public string? SourceText { get; init; }
    [JsonPropertyName("confidence")] public int? Confidence { get; init; }
}

public sealed record ExtractedWarning
{
    [JsonPropertyName("text")] public string? Text { get; init; }

    /// <summary>Generic names this warning refers to. The match target for FR-5.5.</summary>
    [JsonPropertyName("relatesTo")] public IReadOnlyList<string> RelatesTo { get; init; } = [];

    [JsonPropertyName("sourceText")] public string? SourceText { get; init; }
    [JsonPropertyName("confidence")] public int? Confidence { get; init; }
}
