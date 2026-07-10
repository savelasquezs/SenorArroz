using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.Commands;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Application.Features.BranchPrintSettings.Queries;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;
using SenorArroz.API.Services;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/Branches/{branchId:int}/print-settings")]
public class BranchPrintSettingsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly IPrintAgentNotificationService _printAgentNotifications;

    public BranchPrintSettingsController(IMediator mediator, ICurrentUser currentUser, IPrintAgentNotificationService printAgentNotifications)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _printAgentNotifications = printAgentNotifications;
    }

    /// <summary>Sube el logo del ticket (PNG, JPEG, WebP o GIF, máx. 1,5 MB). Campo multipart: file.</summary>
    [HttpPost("receipt-logo")]
    [RequestSizeLimit(1_572_864)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<BranchPrintSettingsDto>>> UploadReceiptLogo(
        int branchId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<BranchPrintSettingsDto>.ErrorResponse("Seleccione un archivo de imagen."));

        if (!TryMapLogoExtension(file.ContentType, file.FileName, out var extension, out var error))
            return BadRequest(ApiResponse<BranchPrintSettingsDto>.ErrorResponse(error));

        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            var result = await _mediator.Send(new UploadBranchReceiptLogoCommand(branchId, bytes, extension), cancellationToken);
            var config = await _mediator.Send(new GetPrintAgentConfigQuery(branchId), cancellationToken);
            if (config is not null)
                await _printAgentNotifications.NotifyConfigChangedAsync(branchId, config, cancellationToken);
            return Ok(ApiResponse<BranchPrintSettingsDto>.SuccessResponse(result, "Logo de ticket actualizado."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<BranchPrintSettingsDto>.ErrorResponse(ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<BranchPrintSettingsDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpDelete("receipt-logo")]
    public async Task<ActionResult<ApiResponse<BranchPrintSettingsDto>>> DeleteReceiptLogo(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        try
        {
            var result = await _mediator.Send(new DeleteBranchReceiptLogoCommand(branchId), cancellationToken);
            var config = await _mediator.Send(new GetPrintAgentConfigQuery(branchId), cancellationToken);
            if (config is not null)
                await _printAgentNotifications.NotifyConfigChangedAsync(branchId, config, cancellationToken);
            return Ok(ApiResponse<BranchPrintSettingsDto>.SuccessResponse(result, "Logo de ticket eliminado."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<BranchPrintSettingsDto>.ErrorResponse(ex.Message));
        }
    }

    private static bool TryMapLogoExtension(string? contentType, string? fileName, out string extensionWithDot, out string error)
    {
        extensionWithDot = ".png";
        error = string.Empty;
        var ct = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        if (ct is "image/png" or "image/x-png")
        {
            extensionWithDot = ".png";
            return true;
        }
        if (ct is "image/jpeg" or "image/jpg" or "image/pjpeg")
        {
            extensionWithDot = ".jpg";
            return true;
        }
        if (ct == "image/webp")
        {
            extensionWithDot = ".webp";
            return true;
        }
        if (ct == "image/gif")
        {
            extensionWithDot = ".gif";
            return true;
        }

        var name = (fileName ?? string.Empty).ToLowerInvariant();
        if (name.EndsWith(".png", StringComparison.Ordinal))
        {
            extensionWithDot = ".png";
            return true;
        }
        if (name.EndsWith(".jpg", StringComparison.Ordinal) || name.EndsWith(".jpeg", StringComparison.Ordinal))
        {
            extensionWithDot = ".jpg";
            return true;
        }
        if (name.EndsWith(".webp", StringComparison.Ordinal))
        {
            extensionWithDot = ".webp";
            return true;
        }
        if (name.EndsWith(".gif", StringComparison.Ordinal))
        {
            extensionWithDot = ".gif";
            return true;
        }

        error = "Tipo de archivo no permitido. Use PNG, JPEG, WebP o GIF.";
        return false;
    }

    [HttpPut]
    public async Task<ActionResult<ApiResponse<BranchPrintSettingsDto>>> Update(
        int branchId,
        [FromBody] UpdateBranchPrintSettingsDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<BranchPrintSettingsDto>.ErrorResponse("Datos inválidos."));

        try
        {
            var result = await _mediator.Send(new UpdateBranchPrintSettingsCommand(branchId, dto), cancellationToken);
            var config = await _mediator.Send(new GetPrintAgentConfigQuery(branchId), cancellationToken);
            if (config is not null)
                await _printAgentNotifications.NotifyConfigChangedAsync(branchId, config, cancellationToken);
            return Ok(ApiResponse<BranchPrintSettingsDto>.SuccessResponse(result, "Configuración de impresión actualizada."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<BranchPrintSettingsDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("rotate-agent-token")]
    public async Task<ActionResult<ApiResponse<RotateBranchAgentTokenResponseDto>>> RotateAgentToken(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        try
        {
            var result = await _mediator.Send(new RotateBranchAgentTokenCommand(branchId), cancellationToken);
            var config = await _mediator.Send(new GetPrintAgentConfigQuery(branchId), cancellationToken);
            if (config is not null)
                await _printAgentNotifications.NotifyConfigChangedAsync(branchId, config, cancellationToken);
            return Ok(ApiResponse<RotateBranchAgentTokenResponseDto>.SuccessResponse(
                result,
                "Token generado. Guárdelo en el agente; no se volverá a mostrar."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<RotateBranchAgentTokenResponseDto>.ErrorResponse(ex.Message));
        }
    }

    private bool CanAccessBranch(int branchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return true;
        return _currentUser.BranchId == branchId;
    }
}
