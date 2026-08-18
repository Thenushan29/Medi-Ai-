namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

/// <summary>Straight-line distance only. Never present this as a road or travel time.</summary>
public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_000;

    public static int HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = DegreesToRadians(lat1);
        var phi2 = DegreesToRadians(lat2);
        var dPhi = DegreesToRadians(lat2 - lat1);
        var dLambda = DegreesToRadians(lon2 - lon1);

        var sinPhi = Math.Sin(dPhi / 2);
        var sinLambda = Math.Sin(dLambda / 2);
        var a = sinPhi * sinPhi + Math.Cos(phi1) * Math.Cos(phi2) * sinLambda * sinLambda;
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return (int)Math.Round(EarthRadiusMeters * c);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
