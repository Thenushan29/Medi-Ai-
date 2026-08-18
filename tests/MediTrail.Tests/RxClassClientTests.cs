using System.Net;
using System.Text;
using MediTrail.Api.AiPipeline.DoctorRecommendation;
using MediTrail.Api.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace MediTrail.Tests;

public class RxClassClientTests
{
    [Fact]
    public async Task MayTreat_Retries_Once_On_429()
    {
        var calls = 0;
        var handler = new ScriptedHandler(request =>
        {
            calls++;
            if (calls == 1)
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            return Json("""{"rxclassDrugInfoList":{"rxclassDrugInfo":[]}}""");
        });
        var client = Create(handler);

        var result = await client.MayTreatAsync("warfarin");

        Assert.False(result.LookupFailed);
        Assert.True(handler.Uris.Count >= 2);
        Assert.Equal(2, handler.Uris.Take(2).Count(uri => uri.ToString().Contains("byDrugName", StringComparison.Ordinal)));
        Assert.All(handler.Uris, uri => Assert.DoesNotContain("interaction", uri.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Miss_Tries_Spelling_Then_ByDrugName_Once()
    {
        var handler = new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.ToString();
            if (path.Contains("spellingsuggestions", StringComparison.Ordinal))
            {
                return Json("""{"suggestionGroup":{"suggestionList":{"suggestion":["warfarin"]}}}""");
            }

            if (path.Contains("drugName=hemaszol", StringComparison.Ordinal))
            {
                return Json("""{"rxclassDrugInfoList":{"rxclassDrugInfo":[]}}""");
            }

            return Json("""{"rxclassDrugInfoList":{"rxclassDrugInfo":[{"rxclassMinConceptItem":{"classId":"D013923","className":"Thromboembolism","classType":"DISEASE"}}]}}""");
        });
        var client = Create(handler);

        var result = await client.MayTreatAsync("hemaszol");

        Assert.False(result.LookupFailed);
        Assert.Contains(handler.Uris, u => u.ToString().Contains("spellingsuggestions", StringComparison.Ordinal));
        Assert.Contains(handler.Uris, u => u.ToString().Contains("byDrugName", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Uris, u => u.ToString().Contains("interaction", StringComparison.OrdinalIgnoreCase));
        var hit = Assert.Single(result.Hits);
        Assert.Equal("Thromboembolism", hit.ClassName);
    }

    [Fact]
    public async Task Transport_Error_Is_Failed_Not_A_Vocabulary_Miss()
    {
        var handler = new ScriptedHandler(_ => throw new HttpRequestException("down"));
        var client = Create(handler);

        var result = await client.MayTreatAsync("warfarin");

        Assert.True(result.LookupFailed);
        Assert.Empty(result.Hits);
    }

    private static RxClassClient Create(ScriptedHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://rxnav.nlm.nih.gov/REST/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        return new RxClassClient(
            http,
            new PassThroughMemoryCache(),
            Options.Create(new DoctorRecommendationOptions()),
            NullLogger<RxClassClient>.Instance);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<Uri> Uris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!);
            return Task.FromResult(reply(request));
        }
    }

    /// <summary>No-op cache so these tests hit the HTTP handler every time.</summary>
    private sealed class PassThroughMemoryCache : IMemoryCache
    {
        public ICacheEntry CreateEntry(object key) => new IgnoreEntry(key);
        public void Dispose() { }
        public void Remove(object key) { }
        public bool TryGetValue(object key, out object? value)
        {
            value = null;
            return false;
        }

        private sealed class IgnoreEntry(object key) : ICacheEntry
        {
            public object Key { get; } = key;
            public object? Value { get; set; }
            public DateTimeOffset? AbsoluteExpiration { get; set; }
            public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
            public TimeSpan? SlidingExpiration { get; set; }
            public IList<IChangeToken> ExpirationTokens { get; } = [];
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = [];
            public CacheItemPriority Priority { get; set; }
            public long? Size { get; set; }
            public void Dispose() { }
        }
    }
}
