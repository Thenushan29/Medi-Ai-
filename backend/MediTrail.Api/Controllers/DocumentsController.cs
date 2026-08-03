using MediTrail.Api.Contracts.Api;
using MediTrail.Api.Middleware;
using MediTrail.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MediTrail.Api.Controllers;

/// <summary>Backs the evidence viewer (§10.9): the source image plus everything read from it.</summary>
[ApiController]
[Route("api/documents")]
[Produces("application/json")]
public sealed class DocumentsController(IDocumentService documents) : ControllerBase
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
}
