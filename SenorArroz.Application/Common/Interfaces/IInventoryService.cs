using SenorArroz.Application.Features.Inventory.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Interfaces;

public interface IInventoryService
{
    Task<IReadOnlyList<ProductAvailabilityDto>> GetAvailabilityAsync(IReadOnlyCollection<int> productIds, int branchId, CancellationToken cancellationToken);
    Task SnapshotAndReserveAsync(Order order, bool deferStrictReservation, string operationKey, CancellationToken cancellationToken);
    Task ConsumeOrderAsync(Order order, string operationKey, CancellationToken cancellationToken);
    Task ReleaseOrderAsync(Order order, string operationKey, CancellationToken cancellationToken);
    Task RecordPurchaseAsync(ExpenseHeader header, string operationKey, CancellationToken cancellationToken);
    Task ReversePurchaseAsync(ExpenseHeader header, string operationKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryBalanceDto>> GetBalancesAsync(int branchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryMovementDto>> GetMovementsAsync(int branchId, int take, CancellationToken cancellationToken);
    Task<InventoryRecipeDto> GetRecipeAsync(int productId, CancellationToken cancellationToken);
    Task<InventoryRecipeDto> SetRecipeAsync(int productId, InventoryControlMode mode, bool enabled, IReadOnlyCollection<InventoryRequirementInput> requirements, CancellationToken cancellationToken);
    Task AdjustAsync(int branchId, InventoryAdjustmentInput input, string operationKey, CancellationToken cancellationToken);
    Task<InventoryCountDto> StartCountAsync(int branchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryCountDto>> GetCountsAsync(int branchId, CancellationToken cancellationToken);
    Task<InventoryCountDto> ConfirmCountAsync(int countId, IReadOnlyCollection<InventoryCountLineInput> lines, string operationKey, CancellationToken cancellationToken);
    Task<InventoryTransferDto> CreateTransferAsync(InventoryTransferInput input, string operationKey, CancellationToken cancellationToken);
    Task<InventoryTransferDto> DispatchTransferAsync(int transferId, string operationKey, CancellationToken cancellationToken);
    Task<InventoryTransferDto> ReceiveTransferAsync(int transferId, ReceiveInventoryTransferInput input, string operationKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryTransferDto>> GetTransfersAsync(int branchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryReportRowDto>> GetReportAsync(int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryDeviationPointDto>> GetDeviationAsync(int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
}

