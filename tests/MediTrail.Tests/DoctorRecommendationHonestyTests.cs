using System.Net;
using System.Text;
using MediTrail.Api.AiPipeline.DoctorRecommendation;
using MediTrail.Api.Configuration;
using MediTrail.Api.Data;
using MediTrail.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MediTrail.Tests;

/// <summary>
/// Mandatory Round 2 honesty tests. Empty is not Failed. LocationNotFound is not Failed.
/// A throwing provider must not invent rows. Cache hits keep the original fetched_at.
/// </summary>
public class DoctorRecommendationHonestyTests : IDisposable
{
    private readonly MediTrailDbContext _db;
    private readonly Guid _patientId = Guid.NewGuid();

    private static readonly string[] OverpassEndpoints =
    [
        "https://one.test/api/interpreter",
        "https://two.test/api/interpreter",
        "https://three.test/api/interpreter"
    ];

    public DoctorRecommendationHonestyTests()
    {
        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseInMemoryDatabase($"honesty-{Guid.NewGuid()}")
            .Options;
        _db = new MediTrailDbContext(options);
        _db.Patients.Add(new Patient { Id = _patientId, DisplayName = "Test" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Empty_Provider_Response_Persists_Zero_Rows_And_Status_Empty()
    {
        var service = CreateService(new StubProvider(new ProviderResult
        {
            Status = ProviderStatus.Empty,
            Facilities = []
        }));

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest { LocationText = "Jaffna" });

        Assert.Equal("empty", response.Status);
        Assert.Equal("empty", response.ProviderStatus);
        Assert.Empty(response.Results);
        Assert.Empty(_db.DoctorSearchResults);
        var stored = Assert.Single(_db.DoctorSearches);
        Assert.Equal("empty", stored.ProviderStatus);
        Assert.Equal(0, stored.ResultCount);
    }

    [Fact]
    public async Task Provider_Exception_Persists_Zero_Rows_And_Status_Failed()
    {
        var service = CreateService(new ThrowingProvider());

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest { LocationText = "Jaffna" });

        Assert.Equal("failed", response.Status);
        Assert.Equal("failed", response.ProviderStatus);
        Assert.NotEqual("empty", response.Status);
        Assert.Empty(response.Results);
        Assert.Empty(_db.DoctorSearchResults);
        var stored = Assert.Single(_db.DoctorSearches);
        Assert.Equal("failed", stored.ProviderStatus);
        Assert.Equal(0, stored.ResultCount);
    }

    [Fact]
    public async Task LocationNotFound_Returns_Distinct_State_Not_Failed()
    {
        var service = new DoctorRecommendationService(
            _db,
            new StubGeocoder(new GeocodeResult { Status = GeocodeStatus.LocationNotFound }),
            new StubProvider(new ProviderResult { Status = ProviderStatus.Ok, Facilities = [] }),
            new StubResolver(),
            new DoctorRankingService(),
            Options.Create(new DoctorRecommendationOptions()));

        var response = await service.SearchAsync(_patientId, new DoctorSearchRequest
        {
            LocationText = "zzzz-not-a-sri-lankan-town"
        });

        Assert.Equal("location_not_found", response.Status);
        Assert.NotEqual("failed", response.Status);
        Assert.NotEqual("empty", response.Status);
        Assert.Empty(response.Results);
        Assert.Null(response.Origin);
        Assert.Empty(_db.DoctorSearchResults);
        Assert.Equal("location_not_found", Assert.Single(_db.DoctorSearches).ProviderStatus);
    }

    [Fact]
    public async Task RxClass_Miss_Falls_Back_To_GP_With_Reason_String()
    {
        var resolver = new SpecialtyResolver(new MissRxClass());

        var result = await resolver.ResolveAsync(new SpecialtyContext
        {
            AlertType = AlertType.DrugInteraction,
            DrugNames = ["hemaszol"]
        });

        Assert.Equal("general_practice", result.Code);
        Assert.Equal("fallback", result.ResolvedBy);
        Assert.Equal("This medication isn't in the NLM RxNorm vocabulary", result.Reason);
        Assert.DoesNotContain("you have", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnosis", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("condition detected", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Overpass_Endpoint1_Down_Falls_Over_To_Endpoint2()
    {
        var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.Host == "one.test")
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            return Json("""{"elements":[{"type":"node","id":1,"lat":9.6615,"lon":80.0255,"tags":{"amenity":"hospital"}}]}""");
        });
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var provider = new OverpassProvider(
            http,
            Options.Create(new DoctorRecommendationOptions
            {
                OverpassEndpoints = OverpassEndpoints,
                OverpassTimeoutSeconds = 5
            }),
            NullLogger<OverpassProvider>.Instance,
            new NoCache());

        var result = await provider.SearchAsync(new ProviderQuery
        {
            Latitude = 9.6615,
            Longitude = 80.0255,
            RadiusMeters = 5000,
            SpecialtyCode = "general_practice"
        });

        Assert.Equal(ProviderStatus.Ok, result.Status);
        Assert.Equal(OverpassEndpoints[1], result.EndpointUsed);
        Assert.Equal(2, handler.Hosts.Count);
        Assert.Equal("one.test", handler.Hosts[0]);
        Assert.Equal("two.test", handler.Hosts[1]);
        var facility = Assert.Single(result.Facilities);
        Assert.Null(facility.Name);
        Assert.Null(facility.Rating);
    }

    [Fact]
    public async Task Cache_Hit_Sets_ServedFromCache_True_And_Preserves_FetchedAt()
    {
        var handler = new ScriptedHandler(_ =>
            Json("""{"elements":[{"type":"node","id":1,"lat":9.6615,"lon":80.0255,"tags":{"amenity":"hospital"}}]}"""));
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var options = Options.Create(new DoctorRecommendationOptions
        {
            OverpassEndpoints = OverpassEndpoints,
            CacheTtlHours = 24
        });
        var provider = new OverpassProvider(
            http,
            options,
            NullLogger<OverpassProvider>.Instance,
            new ProviderCache(_db, options));

        var query = new ProviderQuery
        {
            Latitude = 9.6615,
            Longitude = 80.0255,
            RadiusMeters = 5000,
            SpecialtyCode = "cardiology"
        };

        var first = await provider.SearchAsync(query);
        var second = await provider.SearchAsync(query);

        Assert.False(first.ServedFromCache);
        Assert.True(second.ServedFromCache);
        Assert.Equal(first.FetchedAt, second.FetchedAt);
        Assert.Single(handler.Hosts);
        Assert.Null(Assert.Single(second.Facilities).Name);
    }

    private DoctorRecommendationService CreateService(IDoctorSearchProvider provider) =>
        new(
            _db,
            new StubGeocoder(new GeocodeResult
            {
                Status = GeocodeStatus.Ok,
                ResolvedPlace = "Jaffna",
                Latitude = 9.6615,
                Longitude = 80.0255,
                Geocoder = "static_city_table",
                FetchedAt = DateTimeOffset.UtcNow
            }),
            provider,
            new StubResolver(),
            new DoctorRankingService(),
            Options.Create(new DoctorRecommendationOptions()));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

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

    private sealed class ThrowingProvider : IDoctorSearchProvider
    {
        public string Source => "openstreetmap";

        public Task<ProviderResult> SearchAsync(ProviderQuery query, CancellationToken ct = default) =>
            throw new HttpRequestException("provider down");
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

    private sealed class MissRxClass : IRxClassClient
    {
        public Task<RxClassLookup> MayTreatAsync(string drugName, CancellationToken ct = default) =>
            Task.FromResult(RxClassLookup.Miss());

        public Task<RxClassLookup> AtcClassesAsync(string drugName, CancellationToken ct = default) =>
            Task.FromResult(RxClassLookup.Miss());
    }

    private sealed class NoCache : IProviderCache
    {
        public Task<ProviderResult?> TryGetAsync(
            string keyPrefix, ProviderQuery query, CancellationToken ct = default) =>
            Task.FromResult<ProviderResult?>(null);

        public Task SetAsync(
            string keyPrefix, ProviderQuery query, ProviderResult result, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(reply(request));
        }
    }
}
