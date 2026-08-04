using MediTrail.Api.AiPipeline.Normalization;

namespace MediTrail.Tests;

public class DrugNameNormalizerTests
{
    [Theory]
    [InlineData("Betaloc", "metoprolol")]
    [InlineData("Oxprelol", null)]          // misspelt brand — not in the table, honestly null
    [InlineData("Crocin", "paracetamol")]
    [InlineData("TAB. CROCINE", "paracetamol")]
    [InlineData("Lipitor 10mg", "atorvastatin")]
    [InlineData("Rantac", "ranitidine")]
    [InlineData("DEMO MEDICINE 1", null)]
    [InlineData("Zzyxbrand", null)]
    public void ResolvesKnownBrandsAndAdmitsUnknownOnes(string brand, string? expected) =>
        // Fallback for when the model could not resolve the generic itself. A row with no generic
        // is excluded from every cross-check, so this gap costs findings, not just accuracy.
        Assert.Equal(expected, DrugNameNormalizer.GenericForBrand(brand));

    [Fact]
    public void BrandFallbackFeedsTherapeuticClassDetection() =>
        // The point of the fallback: Betaloc must reach the beta-blocker class, so that
        // prescribing it alongside atenolol is detected as duplicate therapy.
        Assert.Equal("beta blocker", DrugNameNormalizer.ClassOf(DrugNameNormalizer.GenericForBrand("Betaloc")));

    [Theory]
    // The equivalence the whole evaluation dataset turns on (traps.md Y1).
    [InlineData("Paracetamol", "acetaminophen")]
    [InlineData("PARACETAMOL", "Acetaminophen")]
    [InlineData("acetaminophen", "paracetamol")]
    [InlineData("Aspirin", "acetylsalicylic acid")]
    [InlineData("Amoxicillin", "amoxycillin")]
    public void RecognisesSynonymsAsTheSameDrug(string a, string b) =>
        Assert.True(DrugNameNormalizer.AreSameDrug(a, b));

    [Theory]
    [InlineData("paracetamol", "ibuprofen")]
    [InlineData("atenolol", "metoprolol")]
    public void DoesNotConflateDifferentDrugs(string a, string b) =>
        Assert.False(DrugNameNormalizer.AreSameDrug(a, b));

    [Theory]
    // Prescription pads print the dosage form in front of the name.
    [InlineData("TAB. VOMILAST", "vomilast")]
    [InlineData("CAP. ZOCLAR 500", "zoclar")]
    [InlineData("Inj. Dicyclomine HCl", "dicyclomine")]
    [InlineData("Tab Rantac 150mg", "rantac")]
    public void StripsDosageFormAndTrailingStrength(string printed, string expected) =>
        Assert.Equal(expected, DrugNameNormalizer.Normalize(printed));

    [Theory]
    // Clinic-software samples. Treating these as drugs would put a fictional medication in a
    // patient's record (traps.md X6).
    [InlineData("DEMO MEDICINE 1")]
    [InlineData("Demo Medicine 4")]
    [InlineData("SAMPLE DRUG")]
    [InlineData("TEST MEDICINE 2")]
    public void RejectsPlaceholderNames(string placeholder) =>
        Assert.Null(DrugNameNormalizer.Normalize(placeholder));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsNullForNothing(string? input) =>
        Assert.Null(DrugNameNormalizer.Normalize(input));

    [Theory]
    // Three beta-blockers appear across the dataset under different generics (traps.md Y3).
    [InlineData("atenolol", "beta blocker")]
    [InlineData("Metoprolol", "beta blocker")]
    [InlineData("oxprenolol", "beta blocker")]
    [InlineData("amoxicillin", "penicillin")]
    [InlineData("pantoprazole", "proton pump inhibitor")]
    public void ClassifiesTherapeuticClass(string generic, string expected) =>
        Assert.Equal(expected, DrugNameNormalizer.ClassOf(generic));

    [Fact]
    public void ReturnsNoClassForUnknownDrug() =>
        Assert.Null(DrugNameNormalizer.ClassOf("silymarin"));
}

public class FrequencyNormalizerTests
{
    [Theory]
    [InlineData("OD", 1)]
    [InlineData("BD", 2)]
    [InlineData("bid", 2)]
    [InlineData("TDS", 3)]
    [InlineData("t.i.d", 3)]
    [InlineData("QID", 4)]
    [InlineData("Twice daily", 2)]
    [InlineData("Once daily", 1)]
    public void ReadsLatinAbbreviations(string frequency, decimal expected) =>
        Assert.Equal(expected, FrequencyNormalizer.PerDay(frequency));

    [Theory]
    // South Asian morning-afternoon-night notation, as printed in the dataset.
    [InlineData("1-1-1", 3)]
    [InlineData("1-0-1", 2)]
    [InlineData("1-0-0", 1)]
    [InlineData("2-2-2", 6)]
    [InlineData("½-0-½", 1)]
    public void SumsSlotNotation(string frequency, decimal expected) =>
        Assert.Equal(expected, FrequencyNormalizer.PerDay(frequency));

    [Theory]
    [InlineData("1 Morning, 1 Night", 2)]
    [InlineData("1 Morning", 1)]
    [InlineData("1 Morning, 1 Aft, 1 Eve, 1 Night", 4)]
    public void CountsTimesOfDay(string frequency, decimal expected) =>
        Assert.Equal(expected, FrequencyNormalizer.PerDay(frequency));

    [Theory]
    [InlineData("Every 6 hours", 4)]
    [InlineData("q8h", 3)]
    [InlineData("3 times a day", 3)]
    public void ComputesFromIntervals(string frequency, decimal expected) =>
        Assert.Equal(expected, FrequencyNormalizer.PerDay(frequency));

    [Theory]
    // As-needed dosing has no fixed rate. Inventing one would manufacture false dosage conflicts.
    [InlineData("PRN")]
    [InlineData("SOS")]
    [InlineData("as needed")]
    [InlineData("1 od sos")]
    [InlineData("once a week")]
    [InlineData("alternate day")]
    [InlineData(null)]
    [InlineData("")]
    public void ReturnsNullWhenThereIsNoDailyRate(string? frequency) =>
        Assert.Null(FrequencyNormalizer.PerDay(frequency));

    [Fact]
    public void DoesNotMatchAbbreviationsInsideWords() =>
        // "od" must not fire inside "food".
        Assert.Null(FrequencyNormalizer.PerDay("after food"));
}

public class DateNormalizerTests
{
    [Theory]
    [InlineData("2023-08-30", "2023-08-30")]
    [InlineData("30-Aug-2023", "2023-08-30")]
    [InlineData("July 15, 2011", "2011-07-15")]
    [InlineData("27-Apr-2020, 04:37 PM", "2020-04-27")]
    [InlineData("03-Oct-2019, 12:04 PM", "2019-10-03")]
    public void ParsesUnambiguousFormats(string printed, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), DateNormalizer.Parse(printed));

    [Theory]
    // The day exceeds 12, so the arrangement is forced.
    [InlineData("13/04/2022", "2022-04-13")]
    [InlineData("04/13/2022", "2022-04-13")]
    [InlineData("30/08/2023", "2023-08-30")]
    public void ResolvesNumericDatesOnlyWhenForced(string printed, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), DateNormalizer.Parse(printed));

    [Theory]
    // A wrong year silently reorders the timeline, so anything undecidable stays null (FR-4.1).
    [InlineData("01/11/2025")]   // both parts <= 12
    [InlineData("07/10/2022")]   // both parts <= 12
    [InlineData("09-11-12")]     // two-digit year, and day/month ambiguous
    [InlineData("Jan 9, 20yy")]  // placeholder year in the dataset (traps.md Y10)
    [InlineData("March 2022")]   // no day
    [InlineData("")]
    [InlineData(null)]
    public void ReturnsNullWhenAmbiguousOrAbsent(string? printed) =>
        Assert.Null(DateNormalizer.Parse(printed));

    [Fact]
    public void RejectsImpossibleDates() =>
        Assert.Null(DateNormalizer.Parse("31/02/2023"));
}

public class PersonNameNormalizerTests
{
    [Theory]
    // Fragments left beside a censor bar. Presenting these as the prescriber invents a name
    // out of a field the document deliberately hid.
    [InlineData("Dr. Ak")]
    [InlineData("Dr. C")]
    [InlineData("Dr. O")]
    [InlineData("Dr")]
    [InlineData("Dr.")]
    [InlineData("Dr. C█████")]
    [InlineData("Dr. ████ Test")]
    [InlineData("MBBS, MD")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectsRedactedAndFragmentaryNames(string? printed) =>
        Assert.Null(PersonNameNormalizer.Clean(printed));

    [Theory]
    [InlineData("Dr. Meera Iyer", "Dr. Meera Iyer")]
    [InlineData("Dr Rakesh Kumar", "Dr Rakesh Kumar")]
    [InlineData("John M. Brown, M.D.", "John M. Brown, M.D.")]
    [InlineData("Ashraful Mollah", "Ashraful Mollah")]
    [InlineData("கந்தசாமி ராமன்", "கந்தசாமி ராமன்")]
    public void KeepsRealNamesExactlyAsPrinted(string printed, string expected) =>
        // Returns the original string, not the stripped one — the title is part of how the
        // document identifies them.
        Assert.Equal(expected, PersonNameNormalizer.Clean(printed));
}

public class LabTestNormalizerTests
{
    [Theory]
    // A one-sided range has no numeric bounds, so without parsing it the check silently passes.
    // Found by running an unseen prescription through the pipeline: eGFR 52 against "> 90".
    [InlineData(52, "> 90", true)]
    [InlineData(95, "> 90", false)]
    [InlineData(240, "< 200", true)]
    [InlineData(180, "< 200", false)]
    [InlineData(52, "greater than 90", true)]
    [InlineData(4.8, "2.0 - 3.0", false)]      // two-sided text is left to the numeric bounds
    [InlineData(52, "see report", false)]      // unreadable range stays unenforced, never guessed
    [InlineData(52, null, false)]
    public void FlagsValuesOutsideAOneSidedPrintedRange(decimal value, string? rangeText, bool expected) =>
        Assert.Equal(expected, LabTestNormalizer.IsOutOfRange(value, null, null, rangeText));

    [Fact]
    public void PrefersNumericBoundsOverTheTextRange() =>
        Assert.True(LabTestNormalizer.IsOutOfRange(4.8m, 2.0m, 3.0m, "> 90"));

    [Theory]
    // Without this the same test charts as several one-point series and no trend is visible.
    [InlineData("SGPT", "alt")]
    [InlineData("ALT (SGPT)", "alt")]
    [InlineData("Alanine transaminase", "alt")]
    [InlineData("Serum Creatinine", "creatinine")]
    [InlineData("S. Creatinine", "creatinine")]
    [InlineData("Total Bilirubin", "bilirubin total")]
    [InlineData("Haemoglobin", "hemoglobin")]
    [InlineData("HbA1c", "hba1c")]
    public void GroupsAliasesOntoOneKey(string printed, string expected) =>
        Assert.Equal(expected, LabTestNormalizer.Standardize(printed));

    [Fact]
    public void KeepsUnknownTestsUnderTheirOwnCleanedName() =>
        // Still groups with itself across visits, which is what a trend needs.
        Assert.Equal("ferritin", LabTestNormalizer.Standardize("Ferritin"));

    [Theory]
    [InlineData(88, 7, 56, true)]
    [InlineData(30, 7, 56, false)]
    [InlineData(3.4, 0.2, 1.2, true)]
    [InlineData(1.0, 0.7, 1.3, false)]
    public void FlagsValuesOutsideThePrintedRange(double value, double min, double max, bool expected) =>
        Assert.Equal(expected, LabTestNormalizer.IsOutOfRange((decimal)value, (decimal)min, (decimal)max));

    [Fact]
    public void DoesNotFlagWhenTheDocumentPrintedNoRange() =>
        // We only ever flag against a range the document itself supplied (FR-6.3).
        Assert.False(LabTestNormalizer.IsOutOfRange(500, null, null));
}
