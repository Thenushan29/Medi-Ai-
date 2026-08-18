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
    public async Task SuggestSpecialty_Does_Not_Persist_A_Search()
    {
        var service = CreateService(OkJaffna());

        var result = await service.SuggestSpecialtyAsync(_patientId, null, "cardiology");

        Assert.Equal("cardiology", result.Code);
        Assert.Equal("user_override", result.ResolvedBy);
        Assert.Empty(_db.DoctorSearches);
        Assert.Empty(_db.DoctorSearchResults);
    }

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
        Assert.Contains("Jaffna", response.SuggestedPlaces ?? []);
        Assert.DoesNotContain("failed", response.Status);
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

    [Fact]
    public async Task Empty_At_5km_Retries_15km_In_One_Round_Trip()
    {
        var provider = new LadderProvider();
        var service = CreateService(OkJaffna(), provider);

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest
        {
            LocationText = "Jaffna"
        });

        Assert.Equal(new[] { 5000, 15000 }, provider.Radii);
        Assert.Equal([5000, 15000], response.RadiusLadderUsed);
        Assert.Equal(15000, response.RadiusMeters);
        Assert.Equal("ok", response.Status);
        Assert.Equal(40000, response.SuggestedNextRadiusMeters);
        var facility = Assert.Single(response.Results);
        Assert.Null(facility.Name);
        Assert.Equal("node/1", facility.SourceRef);
        Assert.Single(_db.DoctorSearchResults);
        Assert.Null(Assert.Single(_db.DoctorSearchResults).Name);
        Assert.Equal(15000, Assert.Single(_db.DoctorSearches).RadiusMeters);
    }

    [Fact]
    public async Task Ok_At_5km_Does_Not_Widen()
    {
        var provider = new LadderProvider { FillAtMeters = 5000 };
        var service = CreateService(OkJaffna(), provider);

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest { LocationText = "Jaffna" });

        Assert.Equal(new[] { 5000 }, provider.Radii);
        Assert.Equal([5000], response.RadiusLadderUsed);
        Assert.Equal("ok", response.Status);
    }

    [Fact]
    public async Task Empty_Provider_Persists_Zero_Rows()
    {
        var service = CreateService(OkJaffna(), new StubProvider(new ProviderResult
        {
            Status = ProviderStatus.Empty,
            Facilities = []
        }));

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest { LocationText = "Jaffna" });

        Assert.Equal("empty", response.Status);
        Assert.Empty(response.Results);
        Assert.Equal(0, Assert.Single(_db.DoctorSearches).ResultCount);
        Assert.Empty(_db.DoctorSearchResults);
        Assert.Equal(40000, response.SuggestedNextRadiusMeters);
        Assert.False(string.IsNullOrWhiteSpace(response.NearestLargerCity));
        Assert.NotEqual("Jaffna", response.NearestLargerCity);

        var history = await service.ListAsync(_patientId);
        var summary = Assert.Single(history);
        Assert.Equal("general_practice", summary.SpecialtyCode);
        Assert.Equal("General practice", summary.SpecialtyLabel);
        Assert.Equal("empty", summary.ProviderStatus);
        Assert.Equal(0, summary.ResultCount);
    }

    [Fact]
    public async Task Failed_Provider_Persists_Zero_Rows()
    {
        var service = CreateService(OkJaffna(), new StubProvider(new ProviderResult
        {
            Status = ProviderStatus.Failed,
            Facilities = []
        }));

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest { LocationText = "Jaffna" });

        Assert.Equal("failed", response.Status);
        Assert.Empty(response.Results);
        Assert.Empty(_db.DoctorSearchResults);
        Assert.Equal(0, Assert.Single(_db.DoctorSearches).ResultCount);
    }

    [Fact]
    public async Task Failed_With_Stale_Cache_Returns_Labelled_Rows_And_Persists_Zero()
    {
        var fetched = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var service = CreateService(OkJaffna(), new StubProvider(new ProviderResult
        {
            Status = ProviderStatus.Failed,
            StaleCache = true,
            ServedFromCache = true,
            FetchedAt = fetched,
            Facilities =
            [
                new NormalizedFacility
                {
                    Source = "openstreetmap",
                    SourceRef = "node/1",
                    Category = "hospital",
                    Latitude = 9.6615,
                    Longitude = 80.0255,
                    DistanceMeters = 400
                }
            ]
        }));

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest { LocationText = "Jaffna" });

        Assert.Equal("failed", response.Status);
        Assert.True(response.StaleCache);
        Assert.Contains("cached", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fetched, response.FetchedAtUtc);
        var facility = Assert.Single(response.Results);
        Assert.Null(facility.Name);
        Assert.Empty(_db.DoctorSearchResults);
        Assert.Equal(0, Assert.Single(_db.DoctorSearches).ResultCount);
    }

    private static StubGeocoder OkJaffna() =>
        new(new GeocodeResult
        {
            Status = GeocodeStatus.Ok,
            ResolvedPlace = "Jaffna",
            Latitude = 9.6615,
            Longitude = 80.0255,
            Geocoder = "static_city_table",
            FetchedAt = DateTimeOffset.UtcNow
        });

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
        public Task<SpecialtyResolution> ResolveAsync(SpecialtyContext context, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(context.Override))
            {
                var code = context.Override.Trim();
                return Task.FromResult(new SpecialtyResolution
                {
                    Code = code,
                    Label = SpecialtyCatalog.LabelFor(code),
                    ResolvedBy = "user_override",
                    Reason = "Chosen from the specialty list."
                });
            }

            return Task.FromResult(new SpecialtyResolution
            {
                Code = "general_practice",
                Label = "General practice",
                ResolvedBy = "fallback",
                Reason = SpecialtyMaps.NoSignalReason
            });
        }
    }

    private sealed class LadderProvider : IDoctorSearchProvider
    {
        public string Source => "openstreetmap";
        public int FillAtMeters { get; init; } = 15000;
        public List<int> Radii { get; } = [];

        public Task<ProviderResult> SearchAsync(ProviderQuery query, CancellationToken ct = default)
        {
            Radii.Add(query.RadiusMeters);
            if (query.RadiusMeters < FillAtMeters)
            {
                return Task.FromResult(new ProviderResult { Status = ProviderStatus.Empty, Facilities = [] });
            }

            return Task.FromResult(new ProviderResult
            {
                Status = ProviderStatus.Ok,
                FetchedAt = DateTimeOffset.UtcNow,
                Facilities =
                [
                    new NormalizedFacility
                    {
                        Source = "openstreetmap",
                        SourceRef = "node/1",
                        Category = "hospital",
                        Latitude = query.Latitude,
                        Longitude = query.Longitude,
                        DistanceMeters = 0
                    }
                ]
            });
        }
    }
}
