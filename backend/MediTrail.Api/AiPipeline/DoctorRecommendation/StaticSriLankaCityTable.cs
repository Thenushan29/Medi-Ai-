namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

/// <summary>
/// Fallback when Nominatim is empty or down. City coordinates only — not clinics, not doctors.
/// </summary>
public static class StaticSriLankaCityTable
{
    public static IReadOnlyList<(string Name, double Lat, double Lng)> Cities { get; } =
    [
        ("Jaffna", 9.6615, 80.0255),
        ("Colombo", 6.9271, 79.8612),
        ("Kandy", 7.2906, 80.6337),
        ("Galle", 6.0535, 80.2210),
        ("Kurunegala", 7.4863, 80.3623),
        ("Batticaloa", 7.7308, 81.6747),
        ("Trincomalee", 8.5874, 81.2152),
        ("Vavuniya", 8.7514, 80.4971),
        ("Anuradhapura", 8.3114, 80.4037),
        ("Negombo", 7.2008, 79.8737),
        ("Matara", 5.9549, 80.5550),
        ("Ratnapura", 6.7056, 80.3847),
        ("Badulla", 6.9934, 81.0550),
        ("Nuwara Eliya", 6.9497, 80.7891),
        ("Kalutara", 6.5854, 79.9607),
        ("Gampaha", 7.0840, 80.0098),
        ("Panadura", 6.7133, 79.9026),
        ("Chilaw", 7.5758, 79.7953),
        ("Puttalam", 8.0362, 79.8283),
        ("Mannar", 8.9810, 79.9044),
        ("Kilinochchi", 9.3961, 80.3982),
        ("Mullaitivu", 9.2671, 80.8142),
        ("Polonnaruwa", 7.9403, 81.0188),
        ("Ampara", 7.2975, 81.6820),
        ("Hambantota", 6.1246, 81.1185),
        ("Matale", 7.4675, 80.6234),
        ("Kegalle", 7.2513, 80.3464),
        ("Monaragala", 6.8726, 81.3509),
        ("Hatton", 6.8916, 80.5955),
        ("Dehiwala", 6.8402, 79.8712),
        ("Moratuwa", 6.7730, 79.8816),
        ("Point Pedro", 9.8167, 80.2333)
    ];

    public static bool TryResolve(string locationText, out string name, out double lat, out double lng)
    {
        name = string.Empty;
        lat = 0;
        lng = 0;

        var needle = locationText.Trim();
        if (needle.Length == 0) return false;

        var exact = Cities.FirstOrDefault(c =>
            string.Equals(c.Name, needle, StringComparison.OrdinalIgnoreCase));
        if (exact.Name is not null)
        {
            (name, lat, lng) = exact;
            return true;
        }

        var contained = Cities.FirstOrDefault(c =>
            needle.Contains(c.Name, StringComparison.OrdinalIgnoreCase)
            || c.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        if (contained.Name is not null)
        {
            (name, lat, lng) = contained;
            return true;
        }

        return false;
    }

    public static IReadOnlyList<string> SuggestionNames() => Cities.Select(c => c.Name).Take(8).ToList();
}
