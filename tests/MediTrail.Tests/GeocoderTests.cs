using MediTrail.Api.AiPipeline.DoctorRecommendation;
using MediTrail.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediTrail.Tests;

public class GeocoderTests : IDisposable
{
    private readonly MediTrailDbContext _db;

    public GeocoderTests()
    {
        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseInMemoryDatabase($"geo-{Guid.NewGuid()}")
            .Options;
        _db = new MediTrailDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Jaffna_Resolves_From_Static_Table_When_Nominatim_Is_Empty()
    {
        var geocoder = new Geocoder(_db, new StubNominatim(GeocodeStatus.LocationNotFound), NullLogger<Geocoder>.Instance);

        var result = await geocoder.GeocodeAsync("Jaffna");

        Assert.Equal(GeocodeStatus.Ok, result.Status);
        Assert.Equal(9.6615, result.Latitude);
        Assert.Equal(80.0255, result.Longitude);
        Assert.Equal("static_city_table", result.Geocoder);
        Assert.Equal("Jaffna", result.ResolvedPlace);
    }

    [Fact]
    public async Task Unknown_Place_Is_LocationNotFound_Not_Failed()
    {
        var geocoder = new Geocoder(_db, new StubNominatim(GeocodeStatus.LocationNotFound), NullLogger<Geocoder>.Instance);

        var result = await geocoder.GeocodeAsync("zzzz-not-a-sri-lankan-town");

        Assert.Equal(GeocodeStatus.LocationNotFound, result.Status);
        Assert.Null(result.Latitude);
        Assert.Null(result.Longitude);
    }

    [Fact]
    public async Task Nominatim_Failure_Without_City_Match_Is_Failed_Not_LocationNotFound()
    {
        var geocoder = new Geocoder(_db, new StubNominatim(GeocodeStatus.Failed), NullLogger<Geocoder>.Instance);

        var result = await geocoder.GeocodeAsync("zzzz-not-a-sri-lankan-town");

        Assert.Equal(GeocodeStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Second_Jaffna_Lookup_Is_Served_From_Cache_With_FetchedAt()
    {
        var geocoder = new Geocoder(_db, new StubNominatim(GeocodeStatus.LocationNotFound), NullLogger<Geocoder>.Instance);

        var first = await geocoder.GeocodeAsync("Jaffna");
        var second = await geocoder.GeocodeAsync("Jaffna");

        Assert.False(first.ServedFromCache);
        Assert.True(second.ServedFromCache);
        Assert.Equal(first.FetchedAt, second.FetchedAt);
        Assert.Equal(first.Latitude, second.Latitude);
    }

    [Fact]
    public async Task Nominatim_Hit_Is_Preferred_Over_Static_Table()
    {
        var geocoder = new Geocoder(
            _db,
            new StubNominatim(GeocodeStatus.Ok, "Jaffna, Northern Province, Sri Lanka", 9.66, 80.02),
            NullLogger<Geocoder>.Instance);

        var result = await geocoder.GeocodeAsync("Jaffna");

        Assert.Equal("nominatim", result.Geocoder);
        Assert.Equal(9.66, result.Latitude);
        Assert.Equal("Jaffna, Northern Province, Sri Lanka", result.ResolvedPlace);
    }

    [Fact]
    public async Task Tamil_And_Sinhala_Place_Names_Do_Not_Throw()
    {
        var geocoder = new Geocoder(_db, new StubNominatim(GeocodeStatus.LocationNotFound), NullLogger<Geocoder>.Instance);

        var tamil = await geocoder.GeocodeAsync("யாழ்ப்பாணம்");
        var sinhala = await geocoder.GeocodeAsync("යාපනය");

        Assert.Equal(GeocodeStatus.LocationNotFound, tamil.Status);
        Assert.Equal(GeocodeStatus.LocationNotFound, sinhala.Status);
        Assert.Null(tamil.Latitude);
        Assert.Null(sinhala.Latitude);
    }

    private sealed class StubNominatim(GeocodeStatus status, string? place = null, double? lat = null, double? lng = null)
        : INominatimClient
    {
        public Task<NominatimLookup> SearchAsync(string query, CancellationToken ct = default) =>
            Task.FromResult(new NominatimLookup
            {
                Status = status,
                DisplayName = place,
                Latitude = lat,
                Longitude = lng
            });
    }
}
