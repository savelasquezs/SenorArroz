using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.BusinessDocuments.Commands;
using SenorArroz.Application.Features.BusinessDocuments.DTOs;
using SenorArroz.Application.Features.BusinessDocuments.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/business-documents")]
[Authorize]
public sealed class BusinessDocumentsController : ControllerBase
{
    private const long MultipartRequestLimit = 27 * 1024 * 1024;
    private readonly IMediator _mediator;

    public BusinessDocumentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<BusinessDocumentDto>>>> GetDocuments(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortOrder = "asc",
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetBusinessDocumentsQuery(search, page, pageSize, sortBy, sortOrder),
            cancellationToken);
        return Ok(ApiResponse<PagedResult<BusinessDocumentDto>>.SuccessResponse(result));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<BusinessDocumentDto>>> GetDocument(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBusinessDocumentByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<BusinessDocumentDto>.SuccessResponse(result));
    }

    [HttpPost]
    [Authorize(Roles = "Superadmin")]
    [RequestSizeLimit(MultipartRequestLimit)]
    public async Task<ActionResult<ApiResponse<BusinessDocumentDto>>> CreateDocument(
        [FromForm] CreateBusinessDocumentForm form,
        CancellationToken cancellationToken)
    {
        var content = await ReadFileAsync(form.File, cancellationToken);
        var result = await _mediator.Send(
            new CreateBusinessDocumentCommand(
                form.Name,
                content,
                form.File.FileName,
                form.File.ContentType),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetDocument),
            new { id = result.Id },
            ApiResponse<BusinessDocumentDto>.SuccessResponse(
                result,
                "Documento creado correctamente."));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Superadmin")]
    [RequestSizeLimit(MultipartRequestLimit)]
    public async Task<ActionResult<ApiResponse<BusinessDocumentDto>>> UpdateDocument(
        int id,
        [FromForm] UpdateBusinessDocumentForm form,
        CancellationToken cancellationToken)
    {
        byte[]? content = form.File is null
            ? null
            : await ReadFileAsync(form.File, cancellationToken);
        var result = await _mediator.Send(
            new UpdateBusinessDocumentCommand(
                id,
                form.Name,
                content,
                form.File?.FileName,
                form.File?.ContentType),
            cancellationToken);
        return Ok(ApiResponse<BusinessDocumentDto>.SuccessResponse(
            result,
            "Documento actualizado correctamente."));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Superadmin")]
    public async Task<IActionResult> DeleteDocument(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteBusinessDocumentCommand(id), cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("/api/public/business-documents/{publicId:guid}/download")]
    public async Task<IActionResult> Download(Guid publicId, CancellationToken cancellationToken)
    {
        var downloadUrl = await _mediator.Send(
            new GetBusinessDocumentDownloadQuery(publicId),
            cancellationToken);
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        return Redirect(downloadUrl);
    }

    private static async Task<byte[]> ReadFileAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
            return [];
        if (file.Length > 25L * 1024 * 1024)
            throw new BadHttpRequestException("El archivo PDF no puede superar 25 MB.");

        using var memory = new MemoryStream((int)file.Length);
        await using var stream = file.OpenReadStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }
}

public sealed class CreateBusinessDocumentForm
{
    public string Name { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}

public sealed class UpdateBusinessDocumentForm
{
    public string Name { get; set; } = string.Empty;
    public IFormFile? File { get; set; }
}
