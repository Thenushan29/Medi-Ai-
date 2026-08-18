using MediTrail.Api.AiPipeline.DoctorRecommendation;

namespace MediTrail.Tests;

public class AvailabilityMatcherTests
{
    [Fact]
    public void Missing_Hours_Are_Unknown()
    {
        Assert.Equal(AvailabilityMatcher.Unknown, AvailabilityMatcher.MatchRequest(null, "evenings"));
        Assert.Equal(0, AvailabilityMatcher.Score(AvailabilityMatcher.Unknown));
    }

    [Fact]
    public void Always_Open_Matches_Every_Window()
    {
        Assert.Equal(AvailabilityMatcher.Match, AvailabilityMatcher.MatchRequest("24/7", "evenings"));
        Assert.Equal(AvailabilityMatcher.Match, AvailabilityMatcher.MatchRequest("24/7", "weekend"));
        Assert.Equal(10, AvailabilityMatcher.Score(AvailabilityMatcher.Match));
    }

    [Fact]
    public void Closing_At_Or_After_18_Matches_Evenings()
    {
        Assert.Equal(AvailabilityMatcher.Match, AvailabilityMatcher.MatchRequest("Mo-Fr 08:00-20:00", "evenings"));
        Assert.Equal(AvailabilityMatcher.NoMatch, AvailabilityMatcher.MatchRequest("Mo-Fr 08:00-17:00", "evenings"));
    }

    [Fact]
    public void Sa_Or_Su_Matches_Weekend()
    {
        Assert.Equal(AvailabilityMatcher.Match, AvailabilityMatcher.MatchRequest("Sa 09:00-12:00", "weekend"));
        Assert.Equal(AvailabilityMatcher.NoMatch, AvailabilityMatcher.MatchRequest("Mo-Fr 09:00-17:00", "weekend"));
    }

    [Fact]
    public void Anytime_Matches_Any_Tagged_Hours()
    {
        Assert.Equal(AvailabilityMatcher.Match, AvailabilityMatcher.MatchRequest("Mo-Fr 09:00-12:00", "anytime"));
        Assert.Equal(AvailabilityMatcher.Match, AvailabilityMatcher.MatchRequest("Mo-Fr 09:00-12:00", "this_week"));
        Assert.Equal(AvailabilityMatcher.Unknown, AvailabilityMatcher.MatchRequest(null, "anytime"));
    }

    [Fact]
    public void Unparseable_Hours_Are_Indeterminate_Not_A_Penalty()
    {
        Assert.Equal(AvailabilityMatcher.Indeterminate, AvailabilityMatcher.MatchRequest("by appointment", "evenings"));
        Assert.Equal(4, AvailabilityMatcher.Score(AvailabilityMatcher.Indeterminate));
        Assert.Equal(0, AvailabilityMatcher.Score(AvailabilityMatcher.NoMatch));
    }
}

public class DoctorRankingServiceTests
{
    private readonly DoctorRankingService _ranking = new();

    [Fact]
    public void Specialty_Tagged_Result_Outranks_A_Nearer_Untagged_One()
    {
        var nearer = Facility("node/1", category: "hospital", meters: 200);
        var tagged = Facility("node/2", specialty: "cardiology", category: "clinic", meters: 5000);

        var ranked = _ranking.Rank([nearer, tagged], "cardiology", "anytime");

        Assert.Equal("node/2", ranked[0].SourceRef);
        Assert.Equal("node/1", ranked[1].SourceRef);
        Assert.True(ranked[0].RankScore > ranked[1].RankScore);
        Assert.Contains(ranked[0].RankReasons, r => r.Contains("Specialty tag match +40", StringComparison.Ordinal));
        Assert.NotEmpty(ranked[0].RankReasons);
        Assert.NotEmpty(ranked[1].RankReasons);
    }

    [Fact]
    public void Contact_Score_Caps_At_Ten_And_Missing_Name_Stays_Null()
    {
        var facility = Facility(
            "node/3",
            category: "clinic",
            meters: 0,
            phone: "yes",
            website: "https://example.com");

        var ranked = Assert.Single(_ranking.Rank([facility], "cardiology", "anytime"));

        Assert.Null(ranked.Name);
        Assert.Contains(ranked.RankReasons, r => r.Contains("Contact listed +10", StringComparison.Ordinal));
        Assert.DoesNotContain(ranked.RankReasons, r => r.Contains("Website listed", StringComparison.Ordinal));
        Assert.DoesNotContain("diagnosis", string.Join(' ', ranked.RankReasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tie_Break_Is_Shorter_Distance_Then_Name()
    {
        var b = Facility("node/b", name: "Beta", category: "clinic", meters: 1000);
        var a = Facility("node/a", name: "Alpha", category: "clinic", meters: 1000);
        var nearer = Facility("node/n", name: "Near", category: "clinic", meters: 100);

        var ranked = _ranking.Rank([b, a, nearer], "cardiology", "anytime");

        Assert.Equal(["node/n", "node/a", "node/b"], ranked.Select(r => r.SourceRef).ToArray());
    }

    [Fact]
    public void Evening_Match_Adds_Hours_Points()
    {
        var openLate = Facility("node/late", category: "clinic", meters: 0, hours: "Mo-Fr 08:00-20:00");
        var ranked = Assert.Single(_ranking.Rank([openLate], "cardiology", "evenings"));

        Assert.Equal(AvailabilityMatcher.Match, ranked.AvailabilityMatch);
        Assert.Contains(ranked.RankReasons, r => r.Contains("Hours match +10", StringComparison.Ordinal));
        Assert.Contains(ranked.RankReasons, r => r.Contains("km +", StringComparison.Ordinal));
    }

    private static NormalizedFacility Facility(
        string sourceRef,
        string? name = null,
        string? category = null,
        string? specialty = null,
        int meters = 0,
        string? phone = null,
        string? website = null,
        string? hours = null) =>
        new()
        {
            Source = "openstreetmap",
            SourceRef = sourceRef,
            Name = name,
            Category = category,
            SpecialtyTag = specialty,
            Latitude = 9.6615,
            Longitude = 80.0255,
            DistanceMeters = meters,
            Phone = phone,
            Website = website,
            OpeningHours = hours
        };
}
