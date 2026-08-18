using System.Net;
using System.Text;
using MediTrail.Api.AiPipeline.DoctorRecommendation;
using MediTrail.Api.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MediTrail.Tests;

public class OverpassProviderTests
{
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
        SpecialtyCode = "general_practice"
    };

    [Fact]
    public void Query_Uses_Appendix_Around_Filter_And_Amenity_Regex()
    {
        var ql = OverpassProvider.BuildQuery(9.6615, 80.0255, 5000, 25);

        Assert.Contains("[out:json][timeout:25]", ql);
        Assert.Contains("around:5000,9.6615,80.0255", ql);
        Assert.Contains("""["amenity"~"^(doctors|clinic|hospital|pharmacy)$"]""", ql);
        Assert.Contains("""["healthcare"]""", ql);
        Assert.Contains("out center tags", ql);
    }

    [Fact]
    public void Haversine_Is_Zero_At_The_Same_Point_And_About_One_Km_North()
    {
        Assert.Equal(0, GeoMath.HaversineMeters(9.6615, 80.0255, 9.6615, 80.0255));

        // 1000 m due north ≈ 1000 / 111_320 degrees of latitude.
        var north = 9.6615 + 1000.0 / 111_320.0;
        var meters = GeoMath.HaversineMeters(9.6615, 80.0255, north, 80.0255);
        Assert.InRange(meters, 990, 1010);
    }

    [Fact]
    public async Task Search_Posts_Not_Gets()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{"elements":[]}"""));
        var provider = Create(handler);

        await provider.SearchAsync(Jaffna);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("data=", request.Body, StringComparison.Ordinal);
        Assert.Contains("around", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_Elements_Is_Empty_Not_Failed_And_Does_Not_Fail_Over()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{"elements":[]}"""));
        var provider = Create(handler);

        var result = await provider.SearchAsync(Jaffna);

        Assert.Equal(ProviderStatus.Empty, result.Status);
        Assert.Empty(result.Facilities);
        Assert.Equal(Endpoints[0], result.EndpointUsed);
        Assert.NotNull(result.FetchedAt);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Endpoint1_Down_Uses_Endpoint2()
    {
        var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.Host == "one.test")
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            return Json(HttpStatusCode.OK, UnnamedHospitalJson(9.6615, 80.0255));
        });
        var provider = Create(handler);

        var result = await provider.SearchAsync(Jaffna);

        Assert.Equal(ProviderStatus.Ok, result.Status);
        Assert.Equal(Endpoints[1], result.EndpointUsed);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("one.test", handler.Requests[0].RequestUri!.Host);
        Assert.Equal("two.test", handler.Requests[1].RequestUri!.Host);
        var facility = Assert.Single(result.Facilities);
        Assert.Null(facility.Name);
        Assert.Null(facility.Phone);
        Assert.Null(facility.Rating);
        Assert.Equal("node/1", facility.SourceRef);
        Assert.Equal(0, facility.DistanceMeters);
    }

    [Fact]
    public async Task Html_From_Endpoint1_Fails_Over_To_Endpoint2()
    {
        var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.Host == "one.test")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>busy</html>", Encoding.UTF8, "text/html")
                };
            }

            return Json(HttpStatusCode.OK, """{"elements":[]}""");
        });
        var provider = Create(handler);

        var result = await provider.SearchAsync(Jaffna);

        Assert.Equal(ProviderStatus.Empty, result.Status);
        Assert.Equal(Endpoints[1], result.EndpointUsed);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Every_Endpoint_Throwing_Is_Failed_With_Zero_Rows()
    {
        var handler = new ScriptedHandler(_ => throw new HttpRequestException("down"));
        var provider = Create(handler);

        var result = await provider.SearchAsync(Jaffna);

        Assert.Equal(ProviderStatus.Failed, result.Status);
        Assert.Empty(result.Facilities);
        Assert.Null(result.EndpointUsed);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Missing_Tags_Stay_Null_And_Operator_Is_Not_Used_As_Name()
    {
        var json = """
            {"elements":[{
              "type":"way","id":99,
              "center":{"lat":9.670484,"lon":80.0255},
              "tags":{"amenity":"clinic","operator":"public","opening_hours":"24/7","healthcare:speciality":"cardiology"}
            }]}
            """;
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, json));
        var provider = Create(handler);

        var result = await provider.SearchAsync(Jaffna);

        var facility = Assert.Single(result.Facilities);
        Assert.Equal(ProviderStatus.Ok, result.Status);
        Assert.Null(facility.Name);
        Assert.Null(facility.Address);
        Assert.Null(facility.Phone);
        Assert.Null(facility.Website);
        Assert.Null(facility.Rating);
        Assert.Equal("clinic", facility.Category);
        Assert.Equal("cardiology", facility.SpecialtyTag);
        Assert.Equal("24/7", facility.OpeningHours);
        Assert.Equal("way/99", facility.SourceRef);
        Assert.InRange(facility.DistanceMeters, 990, 1010);
    }

    [Fact]
    public async Task Elements_Without_Coordinates_Yield_Empty_Not_Failed()
    {
        var json = """{"elements":[{"type":"way","id":5,"tags":{"amenity":"hospital"}}]}""";
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, json));
        var provider = Create(handler);

        var result = await provider.SearchAsync(Jaffna);

        Assert.Equal(ProviderStatus.Empty, result.Status);
        Assert.Empty(result.Facilities);
    }

    private static OverpassProvider Create(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var options = Options.Create(new DoctorRecommendationOptions
        {
            OverpassEndpoints = Endpoints,
            OverpassTimeoutSeconds = 5
        });
        return new OverpassProvider(http, options, NullLogger<OverpassProvider>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>Unnamed on purpose — fixtures must not contain clinic names, phones, or addresses.</summary>
    private static string UnnamedHospitalJson(double lat, double lng)
    {
        var latS = lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lngS = lng.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "{\"elements\":[{\"type\":\"node\",\"id\":1,\"lat\":" + latS + ",\"lon\":" + lngS + ",\"tags\":{\"amenity\":\"hospital\"}}]}";
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri, body));
            return reply(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string Body);
}
