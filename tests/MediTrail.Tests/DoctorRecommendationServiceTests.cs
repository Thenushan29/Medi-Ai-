using MediTrail.Api.AiPipeline.DoctorRecommendation;
using MediTrail.Api.Configuration;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediTrail.Tests;

public class DoctorRecommendationServiceTests : IDisposable
{
    private readonly MediTrailDbContext _db;
    private readonly Guid _patientId = Guid.NewGuid();

    public DoctorRecommendationServiceTests()
    {
        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseInMemoryDatabase($"docrec-{Guid.NewGuid()}")
            .Options;
        _db = new MediTrailDbContext(options);
        _db.Patients.Add(new Patient { Id = _patientId, DisplayName = "Test" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task LocationNotFound_Persists_Zero_Results_And_Distinct_Status()
    {
        var service = CreateService(new StubGeocoder(new GeocodeResult
        {
            Status = GeocodeStatus.LocationNotFound
        }));

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest
        {
            LocationText = "zzzz-not-a-sri-lankan-town"
        });

        Assert.Equal("location_not_found", response.Status);
        Assert.NotEqual("failed", response.Status);
        Assert.Empty(response.Results);
        Assert.Null(response.Origin);

        var stored = Assert.Single(_db.DoctorSearches);
        Assert.Equal("location_not_found", stored.ProviderStatus);
        Assert.Equal(0, stored.ResultCount);
        Assert.Empty(_db.DoctorSearchResults);
    }

    [Fact]
    public async Task Jaffna_With_Unconfigured_Provider_Does_Not_Invent_Facilities()
    {
        var service = CreateService(new StubGeocoder(new GeocodeResult
        {
            Status = GeocodeStatus.Ok,
            ResolvedPlace = "Jaffna",
            Latitude = 9.6615,
            Longitude = 80.0255,
            Geocoder = "static_city_table",
            FetchedAt = DateTimeOffset.UtcNow
        }));

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest
        {
            LocationText = "Jaffna"
        });

        Assert.Equal("not_configured", response.Status);
        Assert.Empty(response.Results);
        Assert.Equal(9.6615, response.Origin?.Latitude);
        Assert.DoesNotContain("Unknown", response.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private DoctorRecommendationService CreateService(IGeocoder geocoder) =>
        new(
            _db,
            geocoder,
            new NotConfiguredDoctorSearchProvider(),
            Options.Create(new DoctorRecommendationOptions()));

    private sealed class StubGeocoder(GeocodeResult result) : IGeocoder
    {
        public Task<GeocodeResult> GeocodeAsync(string locationText, CancellationToken ct = default) =>
            Task.FromResult(result);
    }
}
