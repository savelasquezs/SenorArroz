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
    private readonly ITenantExecutionContext _tenantExecutionContext;

    public BranchPrintAgentController(IMediator mediator, IPrintQueueService printQueue, ITenantExecutionContext tenantExecutionContext)
    {
        _mediator = mediator;
        _printQueue = printQueue;
        _tenantExecutionContext = tenantExecutionContext;
    }

    /// <summary>Configuración operativa para el agente (colas, flags). Requiere el mismo token que la cola de jobs.</summary>
    [HttpGet("config")]
    public async Task<ActionResult<ApiResponse<PrintAgentConfigDto>>> GetConfig(
        int branchId,
        CancellationToken cancellationToken)
    {
        var token = Request.Headers[BranchPrintJobsController.PrintAgentTokenHeader].FirstOrDefault();
        var identity = await _printQueue.AuthenticateAgentAsync(branchId, token, cancellationToken);
        if (identity is null)
        {
            return Unauthorized(ApiResponse<PrintAgentConfigDto>.ErrorResponse(
                "Token de agente inválido o no configurado."));
        }

        using var tenantScope = _tenantExecutionContext.BeginTenantScope(identity.TenantId);
        var cfg = await _mediator.Send(new GetPrintAgentConfigQuery(branchId), cancellationToken);
        if (cfg is null)
            return NotFound(ApiResponse<PrintAgentConfigDto>.ErrorResponse("Sin configuración de impresión para la sucursal."));

        return Ok(ApiResponse<PrintAgentConfigDto>.SuccessResponse(cfg, "OK"));
    }
}
