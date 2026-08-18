using MediTrail.Api.AiPipeline.DoctorRecommendation;
using MediTrail.Api.Configuration;
using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Middleware;
using MediTrail.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class DoctorSearchController(
    IPatientService patients,
    IDoctorRecommendationService doctors,
    IProviderHealth health,
    IOptions<FeatureOptions> features) : ControllerBase
{
    private readonly bool _enabled = features.Value.DoctorRecommendation;

    [HttpGet("specialties")]
    [ProducesResponseType<IReadOnlyList<SpecialtyOptionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<SpecialtyOptionDto>> Specialties()
    {
        if (!_enabled) return NotFound(Disabled());
        return Ok(doctors.Specialties());
    }

    [HttpGet("patients/{patientId:guid}/specialty-suggestion")]
    [ProducesResponseType<SpecialtyResolutionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpecialtyResolutionDto>> SuggestSpecialty(
        Guid patientId, [FromQuery] Guid? alertId, [FromQuery] string? specialtyOverride, CancellationToken ct)
    {
        if (!_enabled) return NotFound(Disabled());
        if (await patients.GetAsync(patientId, ct) is null) return NotFound(PatientMissing(patientId));
        return Ok(await doctors.SuggestSpecialtyAsync(patientId, alertId, specialtyOverride, ct));
    }

    [HttpGet("patients/{patientId:guid}/doctor-searches")]
    [ProducesResponseType<IReadOnlyList<DoctorSearchSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DoctorSearchSummaryDto>>> List(
        Guid patientId, CancellationToken ct)
    {
        if (!_enabled) return NotFound(Disabled());
        if (await patients.GetAsync(patientId, ct) is null) return NotFound(PatientMissing(patientId));
        return Ok(await doctors.ListAsync(patientId, ct));
    }

    [HttpPost("patients/{patientId:guid}/doctor-search")]
    [ProducesResponseType<DoctorSearchResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoctorSearchResponseDto>> Search(
        Guid patientId, [FromBody] CreateDoctorSearchRequest body, CancellationToken ct)
    {
        if (!_enabled) return NotFound(Disabled());
        if (await patients.GetAsync(patientId, ct) is null) return NotFound(PatientMissing(patientId));

        if (string.IsNullOrWhiteSpace(body.LocationText) && (body.Latitude is null || body.Longitude is null))
        {
            return BadRequest(new ApiError
            {
                Code = "validation_failed",
                Message = "Enter a town or district, or share a location.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var result = await doctors.SearchAsync(patientId, new DoctorSearchRequest
        {
            AlertId = body.AlertId,
            SpecialtyOverride = body.SpecialtyOverride,
            LocationText = body.LocationText ?? string.Empty,
            Latitude = body.Latitude,
            Longitude = body.Longitude,
            Availability = body.Availability,
            RadiusMeters = body.RadiusMeters
        }, ct);

        return Ok(result);
    }

    /// <summary>Venue ping — Overpass, Nominatim, RxNav. Available even when the feature flag is off.</summary>
    [HttpGet("health/providers")]
    [ProducesResponseType<IReadOnlyList<ProviderHealthDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProviderHealthDto>>> Providers(CancellationToken ct) =>
        Ok(await health.PingAsync(ct));

    private ApiError Disabled() => new()
    {
        Code = "not_found",
        Message = "Doctor recommendation is not enabled on this server.",
        TraceId = HttpContext.TraceIdentifier
    };

    private ApiError PatientMissing(Guid id) => new()
    {
        Code = "not_found",
        Message = $"Patient {id} was not found.",
        TraceId = HttpContext.TraceIdentifier
    };
}
