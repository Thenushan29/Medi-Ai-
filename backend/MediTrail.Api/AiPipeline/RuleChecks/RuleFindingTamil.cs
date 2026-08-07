using MediTrail.Api.Data.Entities;

namespace MediTrail.Api.AiPipeline.RuleChecks;

/// <summary>
/// Tamil explanations for the deterministic findings (FR-5.8, Principle 6).
///
/// A rule finding has a fixed shape — a drug name, two doses, a quoted warning — so one template
/// per alert type carries the same meaning as the English explanation without a model call: it is
/// instant, free, and cannot fail halfway through the pipeline the way a generated translation
/// can. The LLM cross-check writes its own Tamil, so nothing here touches it.
///
/// These are not word-for-word translations of the English strings; they say the same thing the
/// way a Tamil speaker would say it. Drug, test and therapeutic-class names stay in the printed
/// Latin form, as pharmacists and patients both write them.
///
/// A template that cannot be completed returns null, and the interface falls back to English.
/// Half a sentence about a medication risk is worse than none.
/// </summary>
public static class RuleFindingTamil
{
    /// <summary>The same generic on two documents with overlapping dates (FR-5.1).</summary>
    public static string DuplicatePrescription(string drugName) =>
        $"{drugName} இரண்டு வெவ்வேறு ஆவணங்களில், ஒன்றையொன்று மேவும் காலகட்டங்களுக்குப் " +
        "பரிந்துரைக்கப்பட்டுள்ளது. இரண்டையும் சேர்த்து எடுத்துக்கொண்டால் இரட்டை அளவு மருந்து " +
        "உடலுக்குச் சென்றுவிடும்.";

    /// <summary>Two or more drugs from one therapeutic class taken together.</summary>
    public static string? DuplicateTherapeuticClass(
        IReadOnlyList<string> drugNames, string therapeuticClass)
    {
        if (drugNames.Count == 0) return null;

        return $"{string.Join(", ", drugNames)} ஆகியவை ஒரே வகையைச் சேர்ந்த மருந்துகள் " +
               $"({therapeuticClass}). இவை உடலில் ஒரே வேலையைத்தான் செய்கின்றன, எனவே " +
               "ஒன்றுக்கு மேற்பட்டவற்றை ஒன்றாக எடுத்துக்கொள்வது எதிர்பார்த்ததை விட மிகக் " +
               "கூடுதலான தாக்கத்தை ஏற்படுத்தலாம்.";
    }

    /// <summary>The same generic at conflicting strength or daily frequency (FR-5.2).</summary>
    public static string DosageConflict(string drugName, Medication a, Medication b) =>
        $"{drugName} ஒரு ஆவணத்தில் {Dose(a)} என்றும், இன்னொரு ஆவணத்தில் {Dose(b)} என்றும் " +
        "எழுதப்பட்டுள்ளது. இந்த இரண்டு அளவுகளும் ஒன்றுக்கொன்று பொருந்தவில்லை.";

    /// <summary>A medication matching a recorded patient allergy (FR-5.4).</summary>
    public static string AllergyConflict(string drugName, string substance, string? reaction)
    {
        var allergy = string.IsNullOrWhiteSpace(reaction)
            ? "ஒவ்வாமை"
            : $"ஒவ்வாமை ({reaction})";

        return $"{drugName} பரிந்துரைக்கப்பட்டுள்ளது; ஆனால் உங்கள் பதிவுகளில் {substance} " +
               $"மருந்துக்கு {allergy} இருப்பதாகக் குறிக்கப்பட்டுள்ளது.";
    }

    /// <summary>
    /// A medication contradicting a warning printed on a document, including the same one
    /// (FR-5.5) — the headline finding in the evaluation dataset.
    /// </summary>
    public static string? DocumentWarningConflict(
        string drugName, string substance, string? warning, bool sameDocument, bool sameMedicine)
    {
        // The quoted sentence is the whole point of this finding. Without it there is nothing to
        // build a Tamil sentence around, so fall back to English rather than gesture at it.
        if (string.IsNullOrWhiteSpace(warning)) return null;

        var opening = sameDocument
            ? $"இதே ஆவணம் {drugName} மருந்தைப் பரிந்துரைக்கிறது; ஆனால் அதன் சொந்த அறிவுரைப் " +
              $"பகுதியிலேயே \"{warning}\" எனக் குறிப்பிடப்பட்டுள்ளது."
            : $"{drugName} பரிந்துரைக்கப்பட்டுள்ளது; ஆனால் உங்கள் பதிவுகளில் உள்ள மற்றொரு " +
              $"ஆவணத்தில் \"{warning}\" எனக் குறிப்பிடப்பட்டுள்ளது.";

        // Two names for one molecule is the contradiction itself; a reader who does not know that
        // sees no conflict at all.
        var equivalence = sameMedicine
            ? $" {substance}-உம் {drugName}-உம் வெவ்வேறு பெயர்களில் அழைக்கப்படும் ஒரே மருந்துதான்."
            : string.Empty;

        return opening + equivalence;
    }

    /// <summary>A value outside the range printed on the report itself (FR-6.3).</summary>
    public static string? LabOutOfRange(
        string testName, decimal? value, string unit, string range, bool above)
    {
        if (value is null) return null;

        var comparison = above ? "விடக் கூடுதலானது" : "விடக் குறைவானது";

        return $"உங்கள் {testName} அளவு {value}{unit} ஆக உள்ளது; இது அறிக்கையில் " +
               $"அச்சிடப்பட்டுள்ள இயல்பான வரம்பை ({range}) {comparison}.";
    }

    /// <summary>Documents that read poorly, so the checks above cannot cover them (FR-5.9).</summary>
    public static string LowExtractionConfidence(int unreadableCount)
    {
        var opening = unreadableCount > 0
            ? $"{unreadableCount} {DocumentWord(unreadableCount)} முற்றிலும் படிக்க முடியவில்லை"
            : "சில ஆவணங்களை ஓரளவுக்கே படிக்க முடிந்தது";

        return $"{opening}; அவற்றில் இருந்த தகவல்கள் மேலே உள்ள சோதனைகளில் விடுபட்டிருக்கலாம். " +
               "படிக்க முடிந்தவற்றை மட்டுமே இந்தக் கண்டுபிடிப்புகள் உள்ளடக்கும்.";
    }

    // ---- helpers ----

    /// <summary>
    /// The dose as the Tamil sentence needs it. The English fragment cannot be reused: dropping
    /// "an unstated strength" into a Tamil clause is exactly the half-built sentence this file
    /// exists to avoid.
    /// </summary>
    private static string Dose(Medication m)
    {
        var strength = m.StrengthValue is not null
            ? $"{m.StrengthValue}{m.StrengthUnit}"
            : "அளவு குறிப்பிடப்படாமல்";

        var frequency = m.FrequencyPerDay is not null
            ? $" நாளொன்றுக்கு {m.FrequencyPerDay} முறை"
            : string.Empty;

        return strength + frequency;
    }

    private static string DocumentWord(int count) => count == 1 ? "ஆவணத்தை" : "ஆவணங்களை";
}
