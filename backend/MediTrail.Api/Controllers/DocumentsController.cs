using MediTrail.Api.AiPipeline;
using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Middleware;
using MediTrail.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MediTrail.Api.Controllers;

/// <summary>Backs the evidence viewer (§10.9): the source image plus everything read from it.</summary>
[ApiController]
[Route("api/documents")]
[Produces("application/json")]
public sealed class DocumentsController(
    IDocumentService documents,
    IPatientAnalyzer analyzer,
    ILogger<DocumentsController> logger) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var document = await documents.GetDocumentAsync(id, ct);

        return document is null
            ? NotFound(new ApiError
            {
                Code = "not_found",
                Message = $"Document {id} was not found.",
                TraceId = HttpContext.TraceIdentifier
            })
            : Ok(document);
    }

    /// <summary>
    /// Removes one uploaded document and everything read from it — for a page uploaded by mistake,
    /// or one belonging to someone else.
    ///
    /// The analysis is re-run afterwards, because alerts are derived: a finding raised from this
    /// document must disappear with it, and one that also rested on other documents must be
    /// recomputed without it. Leaving stale alerts would break the guarantee that every finding
    /// traces to a document you can still open (Principle 3).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var patientId = await documents.DeleteDocumentAsync(id, ct);

        if (patientId is null)
        {
            return NotFound(new ApiError
            {
                Code = "not_found",
                Message = $"Document {id} was not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            await analyzer.AnalyzeAsync(patientId.Value, ct);
        }
        catch (Exception ex)
        {
            // The delete itself succeeded and must not be reported as a failure. Stale findings
            // are recomputed on the next upload.
            logger.LogError(ex, "Re-analysis after deleting {DocumentId} failed", id);
        }

        return NoContent();
    }
}
