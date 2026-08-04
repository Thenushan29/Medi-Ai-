using System.Text.RegularExpressions;

namespace MediTrail.Api.AiPipeline.Normalization;

/// <summary>
/// Resolves drug names to a single comparable key (FR-4.2). This is the join on which every
/// cross-check depends: if <c>Paracetamol</c> and <c>acetaminophen</c> do not collide here, the
/// same-document contradiction in the dataset is never found.
///
/// Deterministic code, not the LLM (Principle 2). The model already proposed a generic name during
/// extraction; this normalizes what it said and applies equivalences that are matters of fact, not
/// of judgement.
/// </summary>
public static partial class DrugNameNormalizer
{
    /// <summary>
    /// Names for the same molecule, mostly INN vs USAN. Not brand mappings — those are the model's
    /// job at extraction time, because they are regional and open-ended. These are closed and safe.
    /// </summary>
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        // The pairing the evaluation dataset turns on.
        ["acetaminophen"] = "paracetamol",
        ["apap"] = "paracetamol",
        ["n-acetyl-p-aminophenol"] = "paracetamol",

        ["salicylic acid, acetyl"] = "aspirin",
        ["acetylsalicylic acid"] = "aspirin",
        ["asa"] = "aspirin",

        ["adrenaline"] = "epinephrine",
        ["noradrenaline"] = "norepinephrine",
        ["salbutamol"] = "albuterol",
        ["frusemide"] = "furosemide",
        ["lignocaine"] = "lidocaine",
        ["amoxycillin"] = "amoxicillin",
        ["cephalexin"] = "cefalexin",
        ["rifampicin"] = "rifampin",
        ["glibenclamide"] = "glyburide",
        ["glyceryl trinitrate"] = "nitroglycerin",
        ["gtn"] = "nitroglycerin",
        ["pethidine"] = "meperidine",
        ["dicyclomine hcl"] = "dicyclomine",
        ["dicycloverine"] = "dicyclomine",
        ["oxprenolol hcl"] = "oxprenolol",
        ["thyroxine"] = "levothyroxine",
        ["vitamin c"] = "ascorbic acid",
        ["vitamin b1"] = "thiamine",
        ["vitamin b6"] = "pyridoxine",
        ["vitamin b9"] = "folic acid",
        ["folate"] = "folic acid",
        ["vitamin b12"] = "cyanocobalamin",
        ["ursodiol"] = "ursodeoxycholic acid",
        ["udca"] = "ursodeoxycholic acid"
    };

    /// <summary>
    /// Therapeutic classes, for duplicate-therapy detection. Prescribing two members of one class
    /// is a real finding even though the generics differ — the dataset carries three beta-blockers.
    /// </summary>
    private static readonly Dictionary<string, string> TherapeuticClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["atenolol"] = "beta blocker",
        ["metoprolol"] = "beta blocker",
        ["oxprenolol"] = "beta blocker",
        ["propranolol"] = "beta blocker",
        ["bisoprolol"] = "beta blocker",
        ["carvedilol"] = "beta blocker",
        ["nebivolol"] = "beta blocker",

        ["omeprazole"] = "proton pump inhibitor",
        ["pantoprazole"] = "proton pump inhibitor",
        ["esomeprazole"] = "proton pump inhibitor",
        ["lansoprazole"] = "proton pump inhibitor",
        ["rabeprazole"] = "proton pump inhibitor",

        ["ranitidine"] = "h2 blocker",
        ["famotidine"] = "h2 blocker",
        ["cimetidine"] = "h2 blocker",

        ["aspirin"] = "nsaid",
        ["ibuprofen"] = "nsaid",
        ["diclofenac"] = "nsaid",
        ["naproxen"] = "nsaid",
        ["mefenamic acid"] = "nsaid",

        ["atorvastatin"] = "statin",
        ["simvastatin"] = "statin",
        ["rosuvastatin"] = "statin",

        ["isosorbide dinitrate"] = "nitrate",
        ["isosorbide mononitrate"] = "nitrate",
        ["nitroglycerin"] = "nitrate",

        ["amoxicillin"] = "penicillin",
        ["ampicillin"] = "penicillin",
        ["penicillin"] = "penicillin",
        ["cloxacillin"] = "penicillin",
        ["piperacillin"] = "penicillin"
    };

    /// <summary>Dosage forms and pack qualifiers that are not part of the name.</summary>
    [GeneratedRegex(@"^\s*(tab|tabs|tablet|cap|caps|capsule|inj|injection|syp|syrup|susp|suspension|oint|ointment|cream|drops|sol|solution)\.?\s+",
        RegexOptions.IgnoreCase)]
    private static partial Regex FormPrefix();

    /// <summary>Trailing strength or release qualifier — "500", "10/SR", "150mg".</summary>
    [GeneratedRegex(@"\s+\d+(\.\d+)?\s*(mg|mcg|g|ml|iu|%)?(\s*/\s*(sr|xl|cr|er|la|md|xr))?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TrailingStrength();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Lowercases, strips dosage form and trailing strength, then applies synonyms.
    /// Returns null for anything empty — never a guess.
    /// </summary>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var cleaned = FormPrefix().Replace(name.Trim(), string.Empty);
        cleaned = TrailingStrength().Replace(cleaned, string.Empty);
        cleaned = Whitespace().Replace(cleaned, " ").Trim().ToLowerInvariant();

        // Placeholders from clinic-software sample documents are not drugs. Treating
        // "DEMO MEDICINE 1" as a medication would put a fictional drug in a patient's record.
        if (IsPlaceholder(cleaned)) return null;

        if (cleaned.Length == 0) return null;

        return Synonyms.TryGetValue(cleaned, out var canonical) ? canonical : cleaned;
    }

    /// <summary>Therapeutic class, or null when the drug is not in the table.</summary>
    public static string? ClassOf(string? genericName)
    {
        var normalized = Normalize(genericName);
        return normalized is not null && TherapeuticClasses.TryGetValue(normalized, out var cls) ? cls : null;
    }

    /// <summary>True when two names denote the same molecule.</summary>
    public static bool AreSameDrug(string? a, string? b)
    {
        var left = Normalize(a);
        var right = Normalize(b);
        return left is not null && right is not null && left == right;
    }

    [GeneratedRegex(@"^(demo|sample|test|dummy|xyz|abc)\b.*|^medicine\s*\d+$|^drug\s*\d+$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();

    /// <summary>
    /// Clinic-software sample documents print "DEMO MEDICINE 1..4". Four documents in the
    /// evaluation dataset are of this kind (traps.md X6).
    /// </summary>
    public static bool IsPlaceholder(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Placeholder().IsMatch(name.Trim());
}
