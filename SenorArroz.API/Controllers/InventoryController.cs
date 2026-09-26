using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Inventory.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public sealed class InventoryController(IInventoryService inventory, IInventoryNotificationService notifications, IBranchContext branchContext) : ControllerBase
{
    [HttpGet("balances")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<IReadOnlyList<InventoryBalanceDto>>> GetBalances([FromQuery] int? branchId, CancellationToken ct)
        => Ok(await inventory.GetBalancesAsync(branchContext.RequireBranch(branchId), ct));

    [HttpGet("movements")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<IReadOnlyList<InventoryMovementDto>>> GetMovements([FromQuery] int? branchId, [FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await inventory.GetMovementsAsync(branchContext.RequireBranch(branchId), take, ct));

    [HttpGet("availability")]
    public async Task<ActionResult<IReadOnlyList<ProductAvailabilityDto>>> GetAvailability([FromQuery] int[] productIds, [FromQuery] int? branchId, CancellationToken ct)
        => Ok(await inventory.GetAvailabilityAsync(productIds, branchContext.RequireBranch(branchId), ct));

    [HttpGet("products/{productId:int}/recipe")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<InventoryRecipeDto>> GetRecipe(int productId, CancellationToken ct)
        => Ok(await inventory.GetRecipeAsync(productId, ct));

    [HttpPut("products/{productId:int}/recipe")]
    [Authorize(Roles = "Superadmin")]
    public async Task<ActionResult<InventoryRecipeDto>> SetRecipe(int productId, [FromBody] SetInventoryRecipeRequest request, CancellationToken ct)
        => Ok(await inventory.SetRecipeAsync(productId, request.ControlMode, request.Enabled, request.Requirements, ct));

    [HttpPost("adjustments")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<IActionResult> Adjust([FromQuery] int? branchId, [FromBody] InventoryAdjustmentInput request, CancellationToken ct)
    {
        var effectiveBranch = branchContext.RequireBranch(branchId);
        await inventory.AdjustAsync(effectiveBranch, request, OperationKey(), ct);
        await notifications.NotifyChangedAsync(effectiveBranch, request.Waste ? "waste" : "adjustment", ct);
        return NoContent();
    }

    [HttpPost("counts")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<InventoryCountDto>> StartCount([FromQuery] int? branchId, CancellationToken ct)
        => Ok(await inventory.StartCountAsync(branchContext.RequireBranch(branchId), ct));

    [HttpGet("counts")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<IReadOnlyList<InventoryCountDto>>> GetCounts([FromQuery] int? branchId, CancellationToken ct)
        => Ok(await inventory.GetCountsAsync(branchContext.RequireBranch(branchId), ct));

    [HttpPut("counts/{countId:int}/draft-lines")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<InventoryCountDto>> SaveCountDraft(int countId, [FromBody] IReadOnlyList<InventoryCountDraftLineInput> lines, CancellationToken ct)
        => Ok(await inventory.SaveCountDraftAsync(countId, lines, ct));

    [HttpPost("counts/{countId:int}/confirm")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<InventoryCountDto>> ConfirmCount(int countId, [FromBody] IReadOnlyList<InventoryCountLineInput> lines, CancellationToken ct)
    {
        var result = await inventory.ConfirmCountAsync(countId, lines, OperationKey(), ct);
        branchContext.EnsureAccess(result.BranchId);
        await notifications.NotifyChangedAsync(result.BranchId, "count", ct);
        return Ok(result);
    }

    [HttpPost("catalog/copy-configuration")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<CopyInventoryConfigurationResult>> CopyCatalogConfiguration([FromBody] CopyInventoryConfigurationRequest request, CancellationToken ct)
        => Ok(await inventory.CopyCatalogConfigurationAsync(request.SourceExpenseId, request.TargetExpenseIds, ct));

    [HttpGet("transfers")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<IReadOnlyList<InventoryTransferDto>>> GetTransfers([FromQuery] int? branchId, CancellationToken ct)
        => Ok(await inventory.GetTransfersAsync(branchContext.RequireBranch(branchId), ct));

    [HttpGet("report")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<IReadOnlyList<InventoryReportRowDto>>> GetReport([FromQuery] int? branchId, [FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
        => Ok(await inventory.GetReportAsync(branchContext.RequireBranch(branchId), fromUtc, toUtc, ct));

    [HttpGet("deviation")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<IReadOnlyList<InventoryDeviationPointDto>>> GetDeviation([FromQuery] int? branchId, [FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
        => Ok(await inventory.GetDeviationAsync(branchContext.RequireBranch(branchId), fromUtc, toUtc, ct));

    [HttpPost("transfers")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<InventoryTransferDto>> CreateTransfer([FromBody] InventoryTransferInput request, CancellationToken ct)
    {
        branchContext.EnsureAccess(request.SourceBranchId);
        return Ok(await inventory.CreateTransferAsync(request, OperationKey(), ct));
    }

    [HttpPost("transfers/{transferId:int}/dispatch")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<InventoryTransferDto>> DispatchTransfer(int transferId, CancellationToken ct)
    {
        var result = await inventory.DispatchTransferAsync(transferId, OperationKey(), ct);
        branchContext.EnsureAccess(result.SourceBranchId);
        await notifications.NotifyChangedAsync(result.SourceBranchId, "transferOut", ct);
        await notifications.NotifyTransferPendingAsync(result.DestinationBranchId, result.Id, ct);
        return Ok(result);
    }

    [HttpPost("transfers/{transferId:int}/receive")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<InventoryTransferDto>> ReceiveTransfer(int transferId, [FromBody] ReceiveInventoryTransferInput request, CancellationToken ct)
    {
        var result = await inventory.ReceiveTransferAsync(transferId, request, OperationKey(), ct);
        branchContext.EnsureAccess(result.DestinationBranchId);
        await notifications.NotifyChangedAsync(result.DestinationBranchId, "transferIn", ct);
        return Ok(result);
    }

    private string OperationKey()
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key)) throw new BusinessException("Idempotency-Key es obligatorio");
        if (key.Length > 120) throw new BusinessException("Idempotency-Key no puede superar 120 caracteres");
        return key.Trim();
    }
}

public sealed record SetInventoryRecipeRequest(InventoryControlMode ControlMode, bool Enabled, IReadOnlyList<InventoryRequirementInput> Requirements);
