using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Common.Interfaces;
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
    private readonly ILogger<BranchPrintJobsController> _logger;

    public BranchPrintJobsController(
        IPrintQueueService printQueue,
        ICurrentUser currentUser,
        ILogger<BranchPrintJobsController> logger)
    {
        _printQueue = printQueue;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>Encola un trabajo de impresión con snapshot del ticket (usuarios de sucursal).</summary>
    [HttpPost]
    [Authorize(Roles = "Superadmin, Admin, Cashier, Kitchen, Deliveryman")]
    public async Task<ActionResult<ApiResponse<EnqueuePrintJobResponse>>> Enqueue(
        int branchId,
        [FromBody] EnqueuePrintJobsRequest request,
        CancellationToken cancellationToken)
    {
        var totalWatch = Stopwatch.StartNew();
        var validationWatch = Stopwatch.StartNew();
        int? deliverymanUserId = null;

        // Domiciliario: la sucursal en la URL debe coincidir con la del pedido, no con user.branch_id
        // (puede trabajar rutas de otra sucursal; antes Forbid() bloqueaba toda la reimpresión desde el celular).
        if (Roles.IsDeliveryman(_currentUser.Role))
        {
            if (request.Kind != PrintJobKind.Delivery)
                return Forbid();
            deliverymanUserId = _currentUser.Id;
        }
        else
        {
            if (!CanAccessBranch(branchId))
                return Forbid();
        }

        if (Roles.IsKitchen(_currentUser.Role)
            && request.Kind != PrintJobKind.Kitchen)
            return Forbid();
        validationWatch.Stop();

        try
        {
            var job = request.Kind == PrintJobKind.Delivery
                ? await _printQueue.EnqueueDeliveryAsync(
                    branchId,
                    request.OrderIds,
                    deliverymanUserId,
                    cancellationToken)
                : await _printQueue.EnqueueAsync(
                    branchId,
                    request.Kind,
                    request.OrderIds,
                    cancellationToken);
            totalWatch.Stop();
            _logger.LogInformation(
                "Print enqueue endpoint completed. PrintJobId={PrintJobId} BranchId={BranchId} Kind={PrintJobKind} RequestValidationElapsedMs={ValidationElapsedMs} EndpointTotalElapsedMs={TotalElapsedMs}.",
                job.Id,
                branchId,
                request.Kind.ToString().ToLowerInvariant(),
                validationWatch.Elapsed.TotalMilliseconds,
                totalWatch.Elapsed.TotalMilliseconds);
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

    /// <summary>Reclama atomically un trabajo pendiente concreto (agente local).</summary>
    [HttpGet("{jobId:long}/claim")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PrintJobAgentItemDto>>> ClaimSpecific(
        int branchId,
        long jobId,
        CancellationToken cancellationToken)
    {
        var token = Request.Headers[PrintAgentTokenHeader].FirstOrDefault();
        if (!await _printQueue.IsAgentTokenValidAsync(branchId, token, cancellationToken))
            return Unauthorized(ApiResponse<PrintJobAgentItemDto>.ErrorResponse("Token de agente invalido o no configurado."));

        var job = await _printQueue.ClaimSpecificForAgentAsync(branchId, jobId, cancellationToken);
        if (job is null)
            return NotFound(ApiResponse<PrintJobAgentItemDto>.ErrorResponse("Trabajo no encontrado o ya reclamado."));

        return Ok(ApiResponse<PrintJobAgentItemDto>.SuccessResponse(job, "OK"));
    }

    /// <summary>Consulta segura del estado de un trabajo, sin exponer el payload.</summary>
    [HttpGet("{jobId:long}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier, Deliveryman")]
    public async Task<ActionResult<ApiResponse<PrintJobStatusDto>>> GetStatus(
        int branchId,
        long jobId,
        CancellationToken cancellationToken)
    {
        int? deliverymanUserId = null;
        if (Roles.IsDeliveryman(_currentUser.Role))
        {
            deliverymanUserId = _currentUser.Id;
        }
        else if (!CanAccessBranch(branchId))
        {
            return Forbid();
        }

        var status = await _printQueue.GetJobStatusAsync(
            branchId,
            jobId,
            deliverymanUserId,
            cancellationToken);
        if (status is null)
            return NotFound(ApiResponse<PrintJobStatusDto>.ErrorResponse("Trabajo no encontrado o no autorizado."));

        return Ok(ApiResponse<PrintJobStatusDto>.SuccessResponse(status, "OK"));
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
