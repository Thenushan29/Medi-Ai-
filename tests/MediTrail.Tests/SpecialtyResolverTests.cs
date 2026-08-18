using MediTrail.Api.AiPipeline.DoctorRecommendation;
using MediTrail.Api.Data.Entities;

namespace MediTrail.Tests;

public class SpecialtyResolverTests
{
    [Fact]
    public async Task Warfarin_Resolves_To_Cardiology_With_Medrt_Evidence()
    {
        var resolver = new SpecialtyResolver(new StubRxClass());

        var result = await resolver.ResolveAsync(new SpecialtyContext
        {
            AlertType = AlertType.DrugInteraction,
            DrugNames = ["warfarin"]
        });

        Assert.Equal("cardiology", result.Code);
        Assert.Equal("rxclass_disease", result.ResolvedBy);
        Assert.Equal(SpecialtyMaps.RxClassDiseaseReason, result.Reason);
        Assert.True(result.AllowPharmacy);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal("Thromboembolism", evidence.Label);
        Assert.Equal("MEDRT", evidence.Source);
        Assert.Equal("D013923", evidence.SourceId);
        Assert.StartsWith("https://mor.nlm.nih.gov/RxClass/", evidence.SourceUrl);
        Assert.DoesNotContain("you have", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnosis", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("condition detected", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_Sri_Lankan_Brand_Falls_Back_To_Gp_With_Rxnorm_Reason()
    {
        var resolver = new SpecialtyResolver(new StubRxClass());

        var result = await resolver.ResolveAsync(new SpecialtyContext
        {
            AlertType = AlertType.DrugInteraction,
            DrugNames = ["hemaszol"]
        });

        Assert.Equal("general_practice", result.Code);
        Assert.Equal("fallback", result.ResolvedBy);
        Assert.Equal(SpecialtyMaps.RxNormMissReason, result.Reason);
        Assert.Equal("This medication isn't in the NLM RxNorm vocabulary", result.Reason);
    }

    [Fact]
    public async Task AllergyConflict_Uses_Alert_Type_Without_RxClass()
    {
        var rx = new StubRxClass();
        var resolver = new SpecialtyResolver(rx);

        var result = await resolver.ResolveAsync(new SpecialtyContext
        {
            AlertType = AlertType.AllergyConflict,
            DrugNames = ["warfarin"]
        });

        Assert.Equal("allergy_immunology", result.Code);
        Assert.Equal("alert_type", result.ResolvedBy);
        Assert.Equal(0, rx.MayTreatCalls);
    }

    [Fact]
    public async Task UnresolvedMedication_Is_General_Practice()
    {
        var resolver = new SpecialtyResolver(new StubRxClass());

        var result = await resolver.ResolveAsync(new SpecialtyContext
        {
            AlertType = AlertType.UnresolvedMedication,
            DrugNames = ["hemaszol"]
        });

        Assert.Equal("general_practice", result.Code);
        Assert.Equal("alert_type", result.ResolvedBy);
        Assert.NotEqual(SpecialtyMaps.RxNormMissReason, result.Reason);
    }

    [Fact]
    public async Task Creatinine_Lab_Alert_Resolves_To_Nephrology()
    {
        var resolver = new SpecialtyResolver(new StubRxClass());

        var result = await resolver.ResolveAsync(new SpecialtyContext
        {
            AlertType = AlertType.LabOutOfRange,
            LabTestKeys = ["creatinine"]
        });

        Assert.Equal("nephrology", result.Code);
        Assert.Equal("alert_type", result.ResolvedBy);
    }

    [Fact]
    public async Task Hba1c_Without_Lab_Alert_Uses_Lab_Rung()
    {
        var resolver = new SpecialtyResolver(new StubRxClass());

        var result = await resolver.ResolveAsync(new SpecialtyContext
        {
            AlertType = AlertType.DocumentWarningConflict,
            LabTestKeys = ["HbA1c"]
        });

        Assert.Equal("endocrinology", result.Code);
        Assert.Equal("lab_test", result.ResolvedBy);
    }

    [Fact]
    public async Task User_Override_Wins_Before_The_Ladder()
    {
        var rx = new StubRxClass();
        var resolver = new SpecialtyResolver(rx);

        var result = await resolver.ResolveAsync(new SpecialtyContext
        {
            AlertType = AlertType.AllergyConflict,
            DrugNames = ["warfarin"],
            Override = "gynaecology"
        });

        Assert.Equal("gynaecology", result.Code);
        Assert.Equal("Gynaecology", result.Label);
        Assert.Equal("user_override", result.ResolvedBy);
        Assert.Equal(0, rx.MayTreatCalls);
    }

    [Fact]
    public async Task Atc_Rung_Fires_When_May_Treat_Has_No_Mapped_Class()
    {
        var resolver = new SpecialtyResolver(new AtcOnlyRxClass());

        var result = await resolver.ResolveAsync(new SpecialtyContext
        {
            DrugNames = ["warfarin"]
        });

        Assert.Equal("cardiology", result.Code);
        Assert.Equal("rxclass_atc", result.ResolvedBy);
        Assert.Equal(SpecialtyMaps.RxClassAtcReason, result.Reason);
    }

    [Fact]
    public void Catalog_Uses_British_Osm_Spellings()
    {
        var codes = SpecialtyCatalog.All.Select(s => s.Code).ToHashSet();
        Assert.Contains("gynaecology", codes);
        Assert.Contains("orthopaedics", codes);
        Assert.Contains("paediatrics", codes);
        Assert.DoesNotContain("gynecology", codes);
        Assert.DoesNotContain("orthopedics", codes);
        Assert.DoesNotContain("pediatrics", codes);
    }

    [Fact]
    public void Round2_Strings_Do_Not_Use_Diagnosis_Language()
    {
        string[] corpus =
        [
            SpecialtyMaps.RxNormMissReason,
            SpecialtyMaps.RxClassUnreachableReason,
            SpecialtyMaps.NoSignalReason,
            SpecialtyMaps.RxClassDiseaseReason,
            SpecialtyMaps.RxClassAtcReason,
            .. SpecialtyCatalog.All.Select(s => s.Label)
        ];

        foreach (var text in corpus)
        {
            Assert.DoesNotContain("you have", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("diagnosis", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("condition detected", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Betaloc_Is_Normalized_To_Metoprolol_Before_RxClass()
    {
        Assert.Equal("metoprolol", RxClassClient.ToQueryName("Betaloc"));
        Assert.Equal("warfarin", RxClassClient.ToQueryName("Warfarin"));
        Assert.Null(RxClassClient.ToQueryName("DEMO MEDICINE 1"));
    }

    private sealed class StubRxClass : IRxClassClient
    {
        public int MayTreatCalls { get; private set; }

        public Task<RxClassLookup> MayTreatAsync(string drugName, CancellationToken ct = default)
        {
            MayTreatCalls++;
            var query = RxClassClient.ToQueryName(drugName);
            if (string.Equals(query, "warfarin", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new RxClassLookup
                {
                    LookupFailed = false,
                    Hits =
                    [
                        new RxClassHit
                        {
                            ClassId = "D013923",
                            ClassName = "Thromboembolism",
                            ClassType = "DISEASE",
                            RelaSource = "MEDRT"
                        }
                    ]
                });
            }

            return Task.FromResult(RxClassLookup.Miss());
        }

        public Task<RxClassLookup> AtcClassesAsync(string drugName, CancellationToken ct = default) =>
            Task.FromResult(RxClassLookup.Miss());
    }

    private sealed class AtcOnlyRxClass : IRxClassClient
    {
        public Task<RxClassLookup> MayTreatAsync(string drugName, CancellationToken ct = default) =>
            Task.FromResult(RxClassLookup.Miss());

        public Task<RxClassLookup> AtcClassesAsync(string drugName, CancellationToken ct = default) =>
            Task.FromResult(new RxClassLookup
            {
                LookupFailed = false,
                Hits =
                [
                    new RxClassHit
                    {
                        ClassId = "B01AA",
                        ClassName = "Vitamin K antagonists",
                        ClassType = "ATC1-4",
                        RelaSource = "ATC"
                    }
                ]
            });
    }
}
