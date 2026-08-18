using System.Net;
using System.Text;
using MediTrail.Api.AiPipeline.DoctorRecommendation;
using MediTrail.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediTrail.Tests;

public class ProviderHealthTests
{
    [Fact]
    public async Task Reports_Ok_And_Failed_Without_Facility_Names()
    {
        var handler = new ScriptedHandler(request =>
        {
            var host = request.RequestUri!.Host;
            if (host.Contains("overpass-api.de", StringComparison.Ordinal)
                || host.Contains("nominatim", StringComparison.Ordinal)
                || host.Contains("rxnav", StringComparison.Ordinal)
                || host.Contains("nlm.nih.gov", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("OK", Encoding.UTF8, "text/plain")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        });

        var health = new ProviderHealth(
            new StubFactory(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) }),
            Options.Create(new DoctorRecommendationOptions()));

        var lines = await health.PingAsync();

        Assert.Equal(3, lines.Count);
        Assert.Equal("overpass", lines[0].Name);
        Assert.Equal("ok", lines[0].Status);
        Assert.NotNull(lines[0].LatencyMs);
        Assert.Equal("ok", lines[1].Status);
        Assert.Equal("ok", lines[2].Status);
        Assert.All(lines, line => Assert.DoesNotContain("hospital", line.Detail ?? "", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubFactory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
