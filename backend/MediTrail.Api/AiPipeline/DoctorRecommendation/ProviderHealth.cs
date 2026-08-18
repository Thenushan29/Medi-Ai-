using System.Diagnostics;
using MediTrail.Api.Configuration;
using MediTrail.Api.Contracts.Api;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

public interface IProviderHealth
{
    Task<IReadOnlyList<ProviderHealthDto>> PingAsync(CancellationToken ct = default);
}

/// <summary>
/// Venue ping: one line each for Overpass, Nominatim, and RxNav, with latency.
/// Does not return facility names.
/// </summary>
public sealed class ProviderHealth(
    IHttpClientFactory httpFactory,
    IOptions<DoctorRecommendationOptions> options) : IProviderHealth
{
    public const string HttpClientName = "ProviderHealth";

    private readonly DoctorRecommendationOptions _options = options.Value;

    public async Task<IReadOnlyList<ProviderHealthDto>> PingAsync(CancellationToken ct = default)
    {
        var overpass = PingOverpassAsync(ct);
        var nominatim = PingGetAsync(
            "nominatim",
            $"{_options.NominatimBaseUrl.TrimEnd('/')}/status",
            ct);
        var rxnav = PingGetAsync(
            "rxnav",
            $"{_options.RxClassBaseUrl.TrimEnd('/')}/version.json",
            ct);

        return [await overpass, await nominatim, await rxnav];
    }

    private async Task<ProviderHealthDto> PingOverpassAsync(CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        Exception? last = null;
        string? lastEndpoint = null;
        var timer = Stopwatch.StartNew();

        foreach (var endpoint in _options.OverpassEndpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) continue;
            lastEndpoint = endpoint;
            timer.Restart();
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["data"] = "[out:json][timeout:8];node(1);out;"
                });
                using var response = await http.PostAsync(endpoint, content, ct);
                var ms = (int)timer.ElapsedMilliseconds;
                if (response.IsSuccessStatusCode)
                {
                    return new ProviderHealthDto
                    {
                        Name = "overpass",
                        Status = "ok",
                        LatencyMs = ms,
                        Endpoint = endpoint
                    };
                }

                last = new HttpRequestException($"HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                last = ex;
            }
        }

        return new ProviderHealthDto
        {
            Name = "overpass",
            Status = "failed",
            LatencyMs = (int)timer.ElapsedMilliseconds,
            Endpoint = lastEndpoint,
            Detail = last?.GetBaseException().Message
        };
    }

    private async Task<ProviderHealthDto> PingGetAsync(string name, string url, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        var timer = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(url, ct);
            var ms = (int)timer.ElapsedMilliseconds;
            if (response.IsSuccessStatusCode)
            {
                return new ProviderHealthDto
                {
                    Name = name,
                    Status = "ok",
                    LatencyMs = ms,
                    Endpoint = url
                };
            }

            return new ProviderHealthDto
            {
                Name = name,
                Status = "failed",
                LatencyMs = ms,
                Endpoint = url,
                Detail = $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new ProviderHealthDto
            {
                Name = name,
                Status = "failed",
                LatencyMs = (int)timer.ElapsedMilliseconds,
                Endpoint = url,
                Detail = ex.GetBaseException().Message
            };
        }
    }
}
