using MediTrail.Api.Configuration;

namespace MediTrail.Api.AiPipeline.Providers;

/// <summary>
/// One place that turns <see cref="AiOptions"/> into a configured <see cref="HttpClient"/>.
/// Shared by the API and the golden-dataset runner so both talk to the provider identically —
/// an accuracy figure measured against different settings than production would be meaningless.
/// </summary>
public static class AiHttpClient
{
    public static void Configure(HttpClient client, AiOptions options)
    {
        client.BaseAddress = new Uri(options.ResolveBaseUrl().TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        // Attribution headers are an OpenRouter feature; other providers ignore them, but there is
        // no reason to send them.
        if (options.Provider != AiProvider.OpenRouter) return;

        if (!string.IsNullOrWhiteSpace(options.SiteUrl))
            client.DefaultRequestHeaders.Add("HTTP-Referer", options.SiteUrl);
        if (!string.IsNullOrWhiteSpace(options.SiteName))
            client.DefaultRequestHeaders.Add("X-Title", options.SiteName);
    }
}
