using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Middleware;
using MediTrail.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MediTrail.Api.Controllers;

/// <summary>
/// Patient profiles and their documents. Controllers stay thin — they validate, delegate to a
/// service, and map to a status code. All logic lives one layer down (§14.2).
/// </summary>
[ApiController]
[Route("api/patients")]
[Produces("application/json")]
public sealed class PatientsController(
    IPatientService patients,
    IDocumentService documents) : ControllerBase
{
    /// <summary>Creates a patient profile (FR-1.1).</summary>
    [HttpPost]
    [ProducesResponseType<PatientDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PatientDetailDto>> Create(CreatePatientRequest request, CancellationToken ct)
    {
        var patient = await patients.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = patient.Id }, patient);
    }

    /// <summary>Lists profiles with document count and last activity (FR-1.2).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PatientSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PatientSummaryDto>>> List(CancellationToken ct) =>
        Ok(await patients.ListAsync(ct));

    /// <summary>Profile detail for the dashboard header (FR-1.3).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PatientDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var patient = await patients.GetAsync(id, ct);
        return patient is null ? NotFound(NotFoundError(id)) : Ok(patient);
    }

    /// <summary>Deletes the profile and everything attached to it, including stored files (FR-1.4).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await patients.DeleteAsync(id, ct) ? NoContent() : NotFound(NotFoundError(id));

    /// <summary>
    /// Multipart upload. Queues processing and returns immediately (FR-2.1, FR-2.8).
    /// Partial success is normal: accepted and rejected files both come back.
    /// </summary>
    [HttpPost("{id:guid}/documents")]
    [ProducesResponseType<UploadResultDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [RequestSizeLimit(120 * 1024 * 1024)]
    public async Task<ActionResult<UploadResultDto>> Upload(
        Guid id,
        [FromForm] IFormFileCollection files,
        [FromForm] string? visitLabel,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
        {
            return BadRequest(new ApiError
            {
                Code = "no_files",
                Message = "Select at least one document to upload.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var result = await documents.UploadAsync(id, files, visitLabel, ct);
        return Accepted(result);
    }

    /// <summary>Processing stage and per-document status; polled every 2s by the processing screen (§10.3).</summary>
    [HttpGet("{id:guid}/status")]
    [ProducesResponseType<ProcessingStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProcessingStatusDto>> Status(Guid id, CancellationToken ct)
    {
        var status = await documents.GetStatusAsync(id, ct);
        return status is null ? NotFound(NotFoundError(id)) : Ok(status);
    }

    /// <summary>Merged chronological timeline (FR-4.5).</summary>
    [HttpGet("{id:guid}/timeline")]
    [ProducesResponseType<IReadOnlyList<TimelineEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TimelineEntryDto>>> Timeline(Guid id, CancellationToken ct)
    {
        if (await patients.GetAsync(id, ct) is null) return NotFound(NotFoundError(id));
        return Ok(await documents.GetTimelineAsync(id, ct));
    }

    private ApiError NotFoundError(Guid id) => new()
    {
        Code = "not_found",
        Message = $"Patient {id} was not found.",
        TraceId = HttpContext.TraceIdentifier
    };
}
