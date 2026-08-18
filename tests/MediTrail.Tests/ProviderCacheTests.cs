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

public class ProviderCacheTests : IDisposable
{
    private readonly MediTrailDbContext _db;

    private static readonly string[] Endpoints =
    [
        "https://one.test/api/interpreter",
        "https://two.test/api/interpreter",
        "https://three.test/api/interpreter"
    ];

    private static readonly ProviderQuery Jaffna = new()
    {
        Latitude = 9.6615,
        Longitude = 80.0255,
        RadiusMeters = 5000,
        SpecialtyCode = "cardiology"
    };

    public ProviderCacheTests()
    {
        var options = new DbContextOptionsBuilder<MediTrailDbContext>()
            .UseInMemoryDatabase($"cache-{Guid.NewGuid()}")
            .Options;
        _db = new MediTrailDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Overpass_Key_Rounds_LatLng_To_Four_Decimals_And_Lowercases_Specialty()
    {
        var key = ProviderCache.BuildKey("overpass", new ProviderQuery
        {
            Latitude = 9.66154,
            Longitude = 80.02546,
            RadiusMeters = 5000,
            SpecialtyCode = "Cardiology"
        });

        Assert.Equal("overpass:9.6615,80.0255:5000:cardiology", key);
    }

    [Fact]
    public async Task Cache_Hit_Sets_ServedFromCache_True_And_Preserves_FetchedAt()
    {
        var handler = new ScriptedHandler(_ => Json("""{"elements":[{"type":"node","id":1,"lat":9.6615,"lon":80.0255,"tags":{"amenity":"hospital"}}]}"""));
        var provider = CreateProvider(handler);

        var first = await provider.SearchAsync(Jaffna);
        var second = await provider.SearchAsync(Jaffna);

        Assert.False(first.ServedFromCache);
        Assert.True(second.ServedFromCache);
        Assert.Equal(first.FetchedAt, second.FetchedAt);
        Assert.Equal(ProviderStatus.Ok, second.Status);
        Assert.Single(handler.Requests);
        var facility = Assert.Single(second.Facilities);
        Assert.Null(facility.Name);
        Assert.Null(facility.Rating);
    }

    [Fact]
    public async Task Empty_Response_Is_Cached_As_Empty()
    {
        var handler = new ScriptedHandler(_ => Json("""{"elements":[]}"""));
        var provider = CreateProvider(handler);

        var first = await provider.SearchAsync(Jaffna);
        var second = await provider.SearchAsync(Jaffna);

        Assert.Equal(ProviderStatus.Empty, first.Status);
        Assert.Equal(ProviderStatus.Empty, second.Status);
        Assert.True(second.ServedFromCache);
        Assert.Equal(first.FetchedAt, second.FetchedAt);
        Assert.Empty(second.Facilities);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Failed_Response_Is_Not_Cached()
    {
        var handler = new ScriptedHandler(_ => throw new HttpRequestException("down"));
        var provider = CreateProvider(handler);

        var first = await provider.SearchAsync(Jaffna);
        var second = await provider.SearchAsync(Jaffna);

        Assert.Equal(ProviderStatus.Failed, first.Status);
        Assert.Equal(ProviderStatus.Failed, second.Status);
        Assert.False(second.ServedFromCache);
        Assert.Equal(6, handler.Requests.Count);
        Assert.Empty(_db.ProviderCache);
    }

    [Fact]
    public async Task Expired_Row_Is_Not_Served_As_Cache()
    {
        _db.ProviderCache.Add(new ProviderCacheEntry
        {
            CacheKey = ProviderCache.BuildKey("overpass", Jaffna),
            Provider = "overpass",
            Payload = System.Text.Json.JsonDocument.Parse("""{"status":"Ok","facilities":[]}"""),
            FetchedAt = DateTimeOffset.UtcNow.AddHours(-48),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-24)
        });
        await _db.SaveChangesAsync();

        var handler = new ScriptedHandler(_ => Json("""{"elements":[]}"""));
        var provider = CreateProvider(handler);

        var result = await provider.SearchAsync(Jaffna);

        Assert.False(result.ServedFromCache);
        Assert.Equal(ProviderStatus.Empty, result.Status);
        Assert.Single(handler.Requests);
    }

    private OverpassProvider CreateProvider(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var options = Options.Create(new DoctorRecommendationOptions
        {
            OverpassEndpoints = Endpoints,
            OverpassTimeoutSeconds = 5,
            CacheTtlHours = 24
        });
        var cache = new ProviderCache(_db, options);
        return new OverpassProvider(http, options, NullLogger<OverpassProvider>.Instance, cache);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(request);
            return reply(request);
        }
    }
}
