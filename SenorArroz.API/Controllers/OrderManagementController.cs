using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Orders.Services;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Roles = "Admin,Superadmin,Cashier")]
public sealed class OrderManagementController(OrderManagementService management, IPrintQueueService printQueue,
    IApplicationDbContext db) : ControllerBase
{
    [HttpGet("remote-branches")]
    public async Task<ActionResult<List<RemoteOrderBranchDto>>> RemoteBranches(CancellationToken ct) =>
        Ok(await management.GetRemoteBranches(ct));

    [HttpGet("{orderId:int}/operation-options")]
    public async Task<ActionResult<OrderOperationOptionsDto>> Options(int orderId, CancellationToken ct) =>
        Ok(await management.GetOptions(orderId, ct));

    [HttpGet("{orderId:int}/tracking")]
    public async Task<ActionResult<OrderTrackingDto>> Tracking(int orderId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await management.GetTracking(orderId, ct));
    }

    public sealed record ReprintRequest(PrintJobKind Kind);
    public sealed record EnqueuedOrderPrint(long JobId);

    [HttpPost("{orderId:int}/print")]
    public async Task<ActionResult<ApiResponse<EnqueuedOrderPrint>>> Reprint(int orderId, ReprintRequest request, CancellationToken ct)
    {
        var order = await management.RequireOrder(orderId, ct);
        if (request.Kind is not PrintJobKind.Kitchen and not PrintJobKind.Delivery)
            return BadRequest(ApiResponse<EnqueuedOrderPrint>.ErrorResponse("Tipo de impresión inválido."));
        try
        {
            var job = request.Kind == PrintJobKind.Kitchen
                ? await printQueue.EnqueueAsync(order.BranchId, request.Kind, [order.Id], ct)
                : await printQueue.EnqueueDeliveryAsync(order.BranchId, [order.Id], cancellationToken: ct);
            return Ok(ApiResponse<EnqueuedOrderPrint>.SuccessResponse(new(job.Id), "Impresión encolada en la sucursal que despacha."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<EnqueuedOrderPrint>.ErrorResponse(ex.Message));
        }
    }

    [HttpGet("{orderId:int}/print/{jobId:long}")]
    public async Task<ActionResult<ApiResponse<PrintJobStatusDto>>> PrintStatus(int orderId, long jobId, CancellationToken ct)
    {
        var order = await management.RequireOrder(orderId, ct);
        var jobOrders = await db.PrintJobs.AsNoTracking().Where(j => j.Id == jobId && j.BranchId == order.BranchId)
            .Select(j => j.OrderIdsJson).SingleOrDefaultAsync(ct);
        if (jobOrders is null || !(JsonSerializer.Deserialize<int[]>(jobOrders) ?? []).SequenceEqual([order.Id]))
            return NotFound();
        var status = await printQueue.GetJobStatusAsync(order.BranchId, jobId, cancellationToken: ct);
        return status is null ? NotFound() : Ok(ApiResponse<PrintJobStatusDto>.SuccessResponse(status, "OK"));
    }
}
