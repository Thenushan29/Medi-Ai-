using System.Globalization;
using MediTrail.Api.Contracts.Api;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

/// <summary>
/// Transparent ranking. Every result has a non-empty reasons array. OSM has no ratings,
/// so rating never enters the score.
/// </summary>
public sealed class DoctorRankingService
{
    public IReadOnlyList<FacilityResultDto> Rank(
        IReadOnlyList<NormalizedFacility> facilities,
        string specialtyCode,
        string? availability)
    {
        return facilities
            .Select(f => Score(f, specialtyCode, availability))
            .OrderByDescending(r => r.RankScore)
            .ThenBy(r => r.DistanceMeters)
            .ThenBy(r => r.Name is null)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FacilityResultDto Score(
        NormalizedFacility facility, string specialtyCode, string? availability)
    {
        var reasons = new List<string>();
        var availabilityMatch = AvailabilityMatcher.MatchRequest(facility.OpeningHours, availability);

        var specialtyScore = SpecialtyScore(facility, specialtyCode, reasons);
        var distanceScore = DistanceScore(facility.DistanceMeters, reasons);
        var contactScore = ContactScore(facility, reasons);
        var availabilityScore = AvailabilityMatcher.Score(availabilityMatch);
        if (availabilityScore == 10) reasons.Add("Hours match +10");
        else if (availabilityScore == 4) reasons.Add("Hours listed +4");

        var typeScore = string.IsNullOrWhiteSpace(facility.Name) ? 0 : 5;
        if (typeScore == 5) reasons.Add("Named facility +5");

        if (reasons.Count == 0)
            reasons.Add("No ranking signals +0");

        return new FacilityResultDto
        {
            SourceRef = facility.SourceRef,
            Name = facility.Name,
            Category = facility.Category,
            SpecialtyTag = facility.SpecialtyTag,
            Address = facility.Address,
            DistanceMeters = facility.DistanceMeters,
            Phone = facility.Phone,
            Website = facility.Website,
            OpeningHours = facility.OpeningHours,
            AvailabilityMatch = availabilityMatch,
            RankScore = specialtyScore + distanceScore + contactScore + availabilityScore + typeScore,
            RankReasons = reasons,
            MapUrl = facility.Source == "openstreetmap"
                ? $"https://www.openstreetmap.org/{facility.SourceRef}"
                : null,
            Latitude = facility.Latitude,
            Longitude = facility.Longitude
        };
    }

    private static int SpecialtyScore(NormalizedFacility facility, string specialtyCode, List<string> reasons)
    {
        if (HasSpecialtyTag(facility.SpecialtyTag, specialtyCode))
        {
            reasons.Add("Specialty tag match +40");
            return 40;
        }

        var category = facility.Category?.Trim().ToLowerInvariant();
        switch (category)
        {
            case "hospital":
                reasons.Add("Hospital +20");
                return 20;
            case "clinic":
                reasons.Add("Clinic +15");
                return 15;
            case "doctors":
                reasons.Add("Doctors +5");
                return 5;
            default:
                return 0;
        }
    }

    private static int DistanceScore(int distanceMeters, List<string> reasons)
    {
        var km = distanceMeters / 1000.0;
        var score = Math.Max(0, (int)Math.Round(30 - km * 2, MidpointRounding.AwayFromZero));
        reasons.Add($"{km.ToString("0.0", CultureInfo.InvariantCulture)} km +{score}");
        return score;
    }

    private static int ContactScore(NormalizedFacility facility, List<string> reasons)
    {
        var phone = !string.IsNullOrWhiteSpace(facility.Phone);
        var website = !string.IsNullOrWhiteSpace(facility.Website);
        if (phone)
        {
            reasons.Add("Contact listed +10");
            return 10;
        }

        if (website)
        {
            reasons.Add("Website listed +5");
            return 5;
        }

        return 0;
    }

    private static bool HasSpecialtyTag(string? specialtyTag, string specialtyCode)
    {
        if (string.IsNullOrWhiteSpace(specialtyTag) || string.IsNullOrWhiteSpace(specialtyCode))
            return false;

        var wanted = Aliases(specialtyCode);
        foreach (var part in specialtyTag.Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (wanted.Contains(part.Replace(' ', '_'), StringComparer.OrdinalIgnoreCase)
                || wanted.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> Aliases(string code)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { code.Replace('_', ' '), code };
        switch (code.Trim().ToLowerInvariant())
        {
            case "general_practice":
                set.Add("general");
                set.Add("gp");
                break;
            case "allergy_immunology":
                set.Add("allergy");
                set.Add("immunology");
                set.Add("allergology");
                break;
            case "gynaecology":
                set.Add("gynecology");
                break;
            case "orthopaedics":
                set.Add("orthopedics");
                break;
            case "paediatrics":
                set.Add("pediatrics");
                break;
        }

        return set;
    }
}
