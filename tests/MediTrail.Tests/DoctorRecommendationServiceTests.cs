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

    [Fact]
    public async Task Cache_Hit_From_Provider_Is_Labelled_And_Keeps_FetchedAt()
    {
        var fetched = new DateTimeOffset(2026, 8, 18, 15, 34, 2, TimeSpan.Zero);
        var service = CreateService(
            new StubGeocoder(new GeocodeResult
            {
                Status = GeocodeStatus.Ok,
                ResolvedPlace = "Jaffna",
                Latitude = 9.6615,
                Longitude = 80.0255,
                Geocoder = "static_city_table"
            }),
            new StubProvider(new ProviderResult
            {
                Status = ProviderStatus.Ok,
                FetchedAt = fetched,
                ServedFromCache = true,
                Facilities =
                [
                    new NormalizedFacility
                    {
                        Source = "openstreetmap",
                        SourceRef = "node/1",
                        Category = "hospital",
                        Latitude = 9.6615,
                        Longitude = 80.0255,
                        DistanceMeters = 0
                    }
                ]
            }));

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest
        {
            LocationText = "Jaffna"
        });

        Assert.True(response.ServedFromCache);
        Assert.Equal(fetched, response.FetchedAtUtc);
        Assert.Null(Assert.Single(response.Results).Name);
        Assert.Equal(fetched, Assert.Single(_db.DoctorSearches).FetchedAt);
        Assert.True(Assert.Single(_db.DoctorSearches).ServedFromCache);
    }

    private DoctorRecommendationService CreateService(IGeocoder geocoder, IDoctorSearchProvider? provider = null) =>
        new(
            _db,
            geocoder,
            provider ?? new NotConfiguredDoctorSearchProvider(),
            new StubResolver(),
            new DoctorRankingService(),
            Options.Create(new DoctorRecommendationOptions()));

    private sealed class StubGeocoder(GeocodeResult result) : IGeocoder
    {
        public Task<GeocodeResult> GeocodeAsync(string locationText, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class StubProvider(ProviderResult result) : IDoctorSearchProvider
    {
        public string Source => "openstreetmap";

        public Task<ProviderResult> SearchAsync(ProviderQuery query, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class StubResolver : ISpecialtyResolver
    {
        public Task<SpecialtyResolution> ResolveAsync(SpecialtyContext context, CancellationToken ct = default) =>
            Task.FromResult(new SpecialtyResolution
            {
                Code = "general_practice",
                Label = "General practice",
                ResolvedBy = "fallback",
                Reason = SpecialtyMaps.NoSignalReason
            });
    }
}
