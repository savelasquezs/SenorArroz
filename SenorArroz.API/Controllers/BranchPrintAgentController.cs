using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Application.Features.BranchPrintSettings.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/branches/{branchId:int}/print-agent")]
public class BranchPrintAgentController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IPrintQueueService _printQueue;

    public BranchPrintAgentController(IMediator mediator, IPrintQueueService printQueue)
    {
        _mediator = mediator;
        _printQueue = printQueue;
    }

    /// <summary>Configuración operativa para el agente (colas, flags). Requiere el mismo token que la cola de jobs.</summary>
    [HttpGet("config")]
    public async Task<ActionResult<ApiResponse<PrintAgentConfigDto>>> GetConfig(
        int branchId,
        CancellationToken cancellationToken)
    {
        var token = Request.Headers[BranchPrintJobsController.PrintAgentTokenHeader].FirstOrDefault();
        if (!await _printQueue.IsAgentTokenValidAsync(branchId, token, cancellationToken))
        {
            return Unauthorized(ApiResponse<PrintAgentConfigDto>.ErrorResponse(
                "Token de agente inválido o no configurado."));
        }

        var cfg = await _mediator.Send(new GetPrintAgentConfigQuery(branchId), cancellationToken);
        if (cfg is null)
            return NotFound(ApiResponse<PrintAgentConfigDto>.ErrorResponse("Sin configuración de impresión para la sucursal."));

        return Ok(ApiResponse<PrintAgentConfigDto>.SuccessResponse(cfg, "OK"));
    }
}
