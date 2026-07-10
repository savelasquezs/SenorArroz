using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.Queries;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

/// <summary>Cola de impresión térmica por sucursal (JWT para encolar; token de agente para pending/complete/fail).</summary>
[ApiController]
[Route("api/branches/{branchId:int}/print-jobs")]
public class BranchPrintJobsController : ControllerBase
{
    public const string PrintAgentTokenHeader = "X-Print-Agent-Token";

    private readonly IPrintQueueService _printQueue;
    private readonly ICurrentUser _currentUser;
    private readonly IPrintAgentNotificationService _printAgentNotifications;
    private readonly IMediator _mediator;

    public BranchPrintJobsController(
        IPrintQueueService printQueue,
        ICurrentUser currentUser,
        IPrintAgentNotificationService printAgentNotifications,
        IMediator mediator)
    {
        _printQueue = printQueue;
        _currentUser = currentUser;
        _printAgentNotifications = printAgentNotifications;
        _mediator = mediator;
    }

    /// <summary>Encola un trabajo de impresión con snapshot del ticket (usuarios de sucursal).</summary>
    [HttpPost]
    [Authorize(Roles = "Superadmin, Admin, Cashier, Kitchen, Deliveryman")]
    public async Task<ActionResult<ApiResponse<EnqueuePrintJobResponse>>> Enqueue(
        int branchId,
        [FromBody] EnqueuePrintJobsRequest request,
        CancellationToken cancellationToken)
    {
        // Domiciliario: la sucursal en la URL debe coincidir con la del pedido, no con user.branch_id
        // (puede trabajar rutas de otra sucursal; antes Forbid() bloqueaba toda la reimpresión desde el celular).
        if (Roles.IsDeliveryman(_currentUser.Role))
        {
            if (request.Kind != PrintJobKind.Delivery)
                return Forbid();
            try
            {
                await _printQueue.ValidateDeliverymanDeliveryEnqueueAsync(
                    branchId,
                    _currentUser.Id,
                    request.OrderIds,
                    cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<EnqueuePrintJobResponse>.ErrorResponse(ex.Message));
            }
        }
        else
        {
            if (!CanAccessBranch(branchId))
                return Forbid();
        }

        if (Roles.IsKitchen(_currentUser.Role)
            && request.Kind != PrintJobKind.Kitchen)
            return Forbid();

        try
        {
            var job = await _printQueue.EnqueueAsync(branchId, request.Kind, request.OrderIds, cancellationToken);
            await NotifyAgentAsync(branchId, cancellationToken);
            return Ok(ApiResponse<EnqueuePrintJobResponse>.SuccessResponse(
                new EnqueuePrintJobResponse(job.Id),
                "Trabajo de impresión encolado."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<EnqueuePrintJobResponse>.ErrorResponse(ex.Message));
        }
    }

    /// <summary>Encola impresión de prueba (payload ficticio; solo administradores de la sucursal).</summary>
    [HttpPost("test")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<EnqueuePrintJobResponse>>> EnqueueTest(
        int branchId,
        [FromBody] EnqueueTestPrintRequest? request,
        CancellationToken cancellationToken)
    {
        if (!CanAccessBranch(branchId))
            return Forbid();

        if (request is null)
            return BadRequest(ApiResponse<EnqueuePrintJobResponse>.ErrorResponse("Cuerpo requerido (kind)."));

        if (request.Kind is not PrintJobKind.Kitchen and not PrintJobKind.Delivery)
            return BadRequest(ApiResponse<EnqueuePrintJobResponse>.ErrorResponse(
                "Indique kind: kitchen o delivery."));

        try
        {
            var job = await _printQueue.EnqueueTestPrintAsync(branchId, request.Kind, cancellationToken);
            await NotifyAgentAsync(branchId, cancellationToken);
            return Ok(ApiResponse<EnqueuePrintJobResponse>.SuccessResponse(
                new EnqueuePrintJobResponse(job.Id),
                "Impresión de prueba encolada."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<EnqueuePrintJobResponse>.ErrorResponse(ex.Message));
        }
    }

    /// <summary>Obtiene y marca como en proceso los trabajos pendientes (agente local).</summary>
    [HttpGet("pending")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PrintJobAgentItemDto>>>> GetPending(
        int branchId,
        [FromQuery] string kinds,
        [FromQuery] int take = 5,
        CancellationToken cancellationToken = default)
    {
        var token = Request.Headers[PrintAgentTokenHeader].FirstOrDefault();
        if (!await _printQueue.IsAgentTokenValidAsync(branchId, token, cancellationToken))
            return Unauthorized(ApiResponse<IReadOnlyList<PrintJobAgentItemDto>>.ErrorResponse("Token de agente inválido o no configurado."));

        if (!TryParseKinds(kinds, out var kindList))
            return BadRequest(ApiResponse<IReadOnlyList<PrintJobAgentItemDto>>.ErrorResponse(
                "Parámetro kinds requerido (ej. kitchen,delivery)."));

        var jobs = await _printQueue.ClaimPendingForAgentAsync(branchId, kindList, take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PrintJobAgentItemDto>>.SuccessResponse(jobs, "OK"));
    }

    [HttpPost("{jobId:long}/complete")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> Complete(int branchId, long jobId, CancellationToken cancellationToken)
    {
        var token = Request.Headers[PrintAgentTokenHeader].FirstOrDefault();
        if (!await _printQueue.IsAgentTokenValidAsync(branchId, token, cancellationToken))
            return Unauthorized(ApiResponse<object>.ErrorResponse("Token de agente inválido."));

        var ok = await _printQueue.TryCompleteJobAsync(branchId, jobId, cancellationToken);
        if (!ok)
            return NotFound(ApiResponse<object>.ErrorResponse("Trabajo no encontrado o no está en procesamiento."));

        return Ok(ApiResponse<object>.SuccessResponse(null!, "Impresión completada."));
    }

    [HttpPost("{jobId:long}/fail")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> Fail(
        int branchId,
        long jobId,
        [FromBody] FailPrintJobRequest body,
        CancellationToken cancellationToken)
    {
        var token = Request.Headers[PrintAgentTokenHeader].FirstOrDefault();
        if (!await _printQueue.IsAgentTokenValidAsync(branchId, token, cancellationToken))
            return Unauthorized(ApiResponse<object>.ErrorResponse("Token de agente inválido."));

        var message = string.IsNullOrWhiteSpace(body.Message) ? "Error desconocido" : body.Message;
        var ok = await _printQueue.TryFailJobAsync(branchId, jobId, message, cancellationToken);
        if (!ok)
            return NotFound(ApiResponse<object>.ErrorResponse("Trabajo no encontrado o no está en procesamiento."));

        return Ok(ApiResponse<object>.SuccessResponse(null!, "Trabajo marcado como fallido."));
    }

    private bool CanAccessBranch(int branchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return true;
        return _currentUser.BranchId == branchId;
    }

    private static bool TryParseKinds(string? kinds, out List<PrintJobKind> list)
    {
        list = new List<PrintJobKind>();
        if (string.IsNullOrWhiteSpace(kinds))
            return false;

        foreach (var part in kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var k = ParseKind(part);
            if (k.HasValue)
                list.Add(k.Value);
        }

        return list.Count > 0;
    }

    private static PrintJobKind? ParseKind(string part)
    {
        return part.ToLowerInvariant() switch
        {
            Roles.Kitchen => PrintJobKind.Kitchen,
            "delivery" => PrintJobKind.Delivery,
            Roles.Cashier => PrintJobKind.Cashier,
            _ => null,
        };
    }

    private async Task NotifyAgentAsync(int branchId, CancellationToken cancellationToken)
    {
        var config = await _mediator.Send(new GetPrintAgentConfigQuery(branchId), cancellationToken);
        if (config is null)
            return;

        await _printAgentNotifications.NotifyConfigChangedAsync(branchId, config, cancellationToken);
        await _printAgentNotifications.NotifyPrintJobsAvailableAsync(branchId, cancellationToken);
    }
}

public class EnqueuePrintJobsRequest
{
    public PrintJobKind Kind { get; set; }
    public List<int> OrderIds { get; set; } = new();
}

public class EnqueueTestPrintRequest
{
    public PrintJobKind Kind { get; set; }
}

public class EnqueuePrintJobResponse
{
    public long JobId { get; set; }

    public EnqueuePrintJobResponse(long jobId) => JobId = jobId;
}

public class FailPrintJobRequest
{
    public string Message { get; set; } = string.Empty;
}
