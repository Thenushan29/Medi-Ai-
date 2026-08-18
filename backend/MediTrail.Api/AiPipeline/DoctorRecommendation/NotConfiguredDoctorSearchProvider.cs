namespace MediTrail.Api.AiPipeline.DoctorRecommendation;

/// <summary>
/// Used when <c>DoctorRecommendation:Provider</c> is not overpass, and in tests.
/// A missing provider is <see cref="ProviderStatus.NotConfigured"/>, not Failed and not Empty.
/// </summary>
public sealed class NotConfiguredDoctorSearchProvider : IDoctorSearchProvider
{
    public string Source => "none";

    public Task<ProviderResult> SearchAsync(ProviderQuery query, CancellationToken ct = default) =>
        Task.FromResult(new ProviderResult { Status = ProviderStatus.NotConfigured });
}
