using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.Commands;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/Branches/{branchId:int}/print-settings")]
public class BranchPrintSettingsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public BranchPrintSettingsController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
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
        if (string.Equals(_currentUser.Role, "superadmin", StringComparison.OrdinalIgnoreCase))
            return true;
        return _currentUser.BranchId == branchId;
    }
}
