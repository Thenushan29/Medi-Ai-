using System.Text.RegularExpressions;
using MediTrail.Api.Data.Entities;

namespace MediTrail.Api.AiPipeline.Normalization;

/// <summary>How two prescriptions stand in relation to each other in time.</summary>
public enum Concurrency
{
    /// <summary>The windows intersect, or both rows came from one document.</summary>
    Overlapping,

    /// <summary>Both dated, and the windows never meet. Not a pair worth cross-checking.</summary>
    NotConcurrent,

    /// <summary>
    /// At least one row has no readable prescribed date. The pair is still checked — dropping it
    /// would hide a real risk behind an extraction failure — but the finding has to say so.
    /// </summary>
    DateUnknown
}

/// <summary>The period a medication was plausibly being taken. A null <see cref="End"/> is open-ended.</summary>
public readonly record struct MedicationWindow(DateOnly? Start, DateOnly? End)
{
    /// <summary>No prescribed date at all — the document's date could not be read.</summary>
    public bool IsUnknown => Start is null;

    public bool IsOpenEnded => Start is not null && End is null;

    public bool Overlaps(MedicationWindow other)
    {
        if (IsUnknown || other.IsUnknown) return false;

        return Start <= (other.End ?? DateOnly.MaxValue)
            && other.Start <= (End ?? DateOnly.MaxValue);
    }
}

/// <summary>
/// Derives each medication's active window and compares two of them (§11.1, stage 4 gate).
///
/// Cross-checking a patient's *entire* history pairwise treats every drug they have ever been
/// given as if it were in the cabinet today: atenolol from 2007 pairs with methylphenidate from
/// 2023 and the alert list fills with combinations that never existed. That is clinically
/// misleading, and the noise buries the same-visit findings the dataset actually plants.
///
/// Deterministic, like every other date and unit decision in the pipeline (Principle 2).
/// </summary>
public static partial class MedicationWindowCalculator
{
    /// <summary>
    /// Assumed course length when the document printed no duration and nothing in the frequency or
    /// instructions says the drug continues.
    ///
    /// Any value here is a guess, so it is chosen to fail in the safe direction: 30 days is longer
    /// than every acute course in the evaluation dataset, so it will not hide a genuine overlap,
    /// and it is finite, so a single prescription from years ago stops being "active" forever and
    /// pairing with everything that came after it. Treating an absent duration as indefinite is
    /// the assumption that produced the noise in the first place.
    /// </summary>
    public const int AssumedCourseDays = 30;

    /// <summary>
    /// Said on any finding that rests on a pair whose concurrency could not be established.
    /// Surfacing the uncertainty is the point — a silent drop and a confident claim are both worse
    /// (Principle 1).
    /// </summary>
    public const string DateUnknownCaveatEn =
        "One of these prescriptions has no readable date, so whether they were being taken at the " +
        "same time could not be determined.";

    /// <summary>
    /// The Tamil half of the caveat. It lives here rather than in RuleFindingTamil because both the
    /// deterministic checks and the LLM cross-check append the same fixed sentence — the cross-check
    /// writes its own Tamil for everything else, but not for this, which is our statement and not
    /// the model's.
    /// </summary>
    public const string DateUnknownCaveatTa =
        "இவற்றில் ஒரு ஆவணத்தில் தேதியைப் படிக்க முடியவில்லை; எனவே இவை ஒரே காலகட்டத்தில் " +
        "எடுக்கப்பட்டனவா என்பதைத் தீர்மானிக்க முடியவில்லை.";

    /// <summary>
    /// [prescribed date, prescribed date + duration]. The merger already derives
    /// <see cref="Medication.EndDate"/> that way; it is recomputed from
    /// <see cref="Medication.DurationDays"/> when absent so the calculator works on a row built
    /// either way.
    /// </summary>
    public static MedicationWindow For(Medication medication)
    {
        if (medication.StartDate is null) return new MedicationWindow(null, null);

        var start = medication.StartDate.Value;

        var end = medication.EndDate
            ?? (medication.DurationDays is > 0 ? start.AddDays(medication.DurationDays.Value - 1) : null);

        if (end is not null) return new MedicationWindow(start, end);

        // No printed duration. Open-ended only when the instructions themselves say the drug has no
        // stop date — "as and when required", "continue", a maintenance dose. A sublingual nitrate
        // taken as needed genuinely is still in use years later, and cutting it off at 30 days
        // would hide a real interaction.
        if (IsOngoing(medication)) return new MedicationWindow(start, null);

        return new MedicationWindow(start, start.AddDays(AssumedCourseDays - 1));
    }

    /// <summary>
    /// Whether two rows are close enough in time to be worth cross-checking against each other.
    /// </summary>
    public static Concurrency Compare(Medication a, Medication b)
    {
        // Two rows on one document are one prescribing decision seen whole. They are concurrent by
        // definition, whatever their dates say — the same-document contradiction (FR-5.5,
        // traps.md Y1) and two beta-blockers on one page (Y4) must survive any date reasoning,
        // including a document whose date could not be read at all.
        if (a.DocumentId == b.DocumentId) return Concurrency.Overlapping;

        var (windowA, windowB) = (For(a), For(b));

        if (windowA.IsUnknown || windowB.IsUnknown) return Concurrency.DateUnknown;

        return windowA.Overlaps(windowB) ? Concurrency.Overlapping : Concurrency.NotConcurrent;
    }

    /// <summary>
    /// The strongest relation between any row of one group and any row of another — two generics
    /// are concurrent if *some* pair of their prescriptions was.
    /// </summary>
    public static Concurrency Compare(IEnumerable<Medication> a, IEnumerable<Medication> b)
    {
        var verdict = Concurrency.NotConcurrent;

        foreach (var x in a)
        {
            foreach (var y in b)
            {
                switch (Compare(x, y))
                {
                    case Concurrency.Overlapping:
                        return Concurrency.Overlapping;
                    case Concurrency.DateUnknown:
                        verdict = Concurrency.DateUnknown;
                        break;
                }
            }
        }

        return verdict;
    }

    /// <summary>The window in words, for the medication list the cross-check model is grounded on.</summary>
    public static string Describe(Medication medication)
    {
        var window = For(medication);

        if (window.IsUnknown) return "date not readable";
        if (window.IsOpenEnded) return $"from {window.Start:yyyy-MM-dd}, no end date printed";

        return $"{window.Start:yyyy-MM-dd} to {window.End:yyyy-MM-dd}";
    }

    /// <summary>
    /// Wording that marks a drug as continuing rather than a fixed course: as-needed dosing, an
    /// explicit instruction to continue, or a maintenance regimen. Matched on word boundaries so
    /// "prn" does not fire inside another word.
    /// </summary>
    [GeneratedRegex(
        @"\b(as\s+needed|as\s+required|when\s+required|if\s+needed|as\s+and\s+when|prn|s\.?o\.?s|" +
        @"continue|continued|continuing|continuous|continuously|ongoing|long[\s-]?term|" +
        @"life[\s-]?long|lifelong|maintenance|indefinitely|regularly|daily\s+for\s+life)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex OngoingMarker();

    private static bool IsOngoing(Medication medication) =>
        (medication.Frequency is { } frequency && OngoingMarker().IsMatch(frequency))
        || (medication.Instructions is { } instructions && OngoingMarker().IsMatch(instructions));
}
