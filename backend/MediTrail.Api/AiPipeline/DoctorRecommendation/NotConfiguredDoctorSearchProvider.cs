using MediTrail.Api.Contracts.Api;

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

/// <summary>Canonical override list for the drawer dropdown (T6 will expand the map).</summary>
public static class SpecialtyCatalog
{
    public static readonly IReadOnlyList<SpecialtyOptionDto> All =
    [
        new() { Code = "general_practice", Label = "General practice" },
        new() { Code = "cardiology", Label = "Cardiology" },
        new() { Code = "endocrinology", Label = "Endocrinology" },
        new() { Code = "nephrology", Label = "Nephrology" },
        new() { Code = "allergy_immunology", Label = "Allergy / immunology" },
        new() { Code = "gynaecology", Label = "Gynaecology" },
        new() { Code = "orthopaedics", Label = "Orthopaedics" },
        new() { Code = "paediatrics", Label = "Paediatrics" }
    ];
}
