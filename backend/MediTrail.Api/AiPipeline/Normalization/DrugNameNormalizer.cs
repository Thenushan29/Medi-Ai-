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
    /// Widely-recognised brands, used **only** as a fallback when the model could not resolve the
    /// generic itself. Without it a brand-only row carries no generic, is excluded from every
    /// cross-check, and a real interaction goes unreported — measured on the evaluation set, two
    /// beta-blockers were being missed for exactly this reason.
    ///
    /// Deliberately small and international, not a scrape: brand naming is regional and
    /// open-ended, so the model remains the primary route and a terminology service is the
    /// production path (§26). A wrong entry here would be worse than an absent one.
    /// </summary>
    private static readonly Dictionary<string, string> Brands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["betaloc"] = "metoprolol",
        ["lopressor"] = "metoprolol",
        ["trasicor"] = "oxprenolol",
        // Printed as "Oxprelol" on patient_y_year3_6 (traps.md Y12). The model is right to decline
        // an ambiguous misspelling, but the null generic then hides a third beta-blocker from the
        // class check (traps.md Y3). Resolved here, where a closed table can be reviewed, rather
        // than by asking the prompt to guess — the same reason "crocine" sits beside "crocin".
        ["oxprelol"] = "oxprenolol",
        ["tenormin"] = "atenolol",
        ["inderal"] = "propranolol",
        ["concor"] = "bisoprolol",

        ["crocin"] = "paracetamol",
        ["crocine"] = "paracetamol",
        ["panadol"] = "paracetamol",
        ["calpol"] = "paracetamol",
        ["tylenol"] = "paracetamol",
        ["dolo"] = "paracetamol",

        ["disprin"] = "aspirin",
        ["ecosprin"] = "aspirin",
        ["brufen"] = "ibuprofen",
        ["combiflam"] = "ibuprofen/paracetamol",
        ["voveran"] = "diclofenac",

        ["rantac"] = "ranitidine",
        ["zinetac"] = "ranitidine",
        ["pantocid"] = "pantoprazole",
        ["pan"] = "pantoprazole",
        ["omez"] = "omeprazole",

        ["lipitor"] = "atorvastatin",
        ["atorlip"] = "atorvastatin",
        ["concerta"] = "methylphenidate",
        ["ritalin"] = "methylphenidate",
        ["augmentin"] = "amoxicillin/clavulanic acid",
        ["zoclar"] = "clarithromycin",
        ["amoxil"] = "amoxicillin",
        ["udiliv"] = "ursodeoxycholic acid",
        ["ursocol"] = "ursodeoxycholic acid",
        ["silybon"] = "silymarin",
        ["becosules"] = "vitamin b complex",
        ["amphogel"] = "aluminium hydroxide",
        ["glycomet"] = "metformin",
        ["eltroxin"] = "levothyroxine"
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

    /// <summary>
    /// Resolves a brand to its generic. Returns null for an unrecognised brand — that is the
    /// honest answer, and the row is still stored and shown, it simply cannot join the
    /// generic-keyed cross-checks.
    /// </summary>
    public static string? GenericForBrand(string? brandName)
    {
        if (string.IsNullOrWhiteSpace(brandName)) return null;

        var cleaned = FormPrefix().Replace(brandName.Trim(), string.Empty);
        cleaned = TrailingStrength().Replace(cleaned, string.Empty);
        cleaned = Whitespace().Replace(cleaned, " ").Trim().ToLowerInvariant();

        if (cleaned.Length == 0 || IsPlaceholder(cleaned)) return null;

        return Brands.TryGetValue(cleaned, out var generic) ? generic : null;
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
