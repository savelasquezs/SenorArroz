using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Inventory.DTOs;

public sealed record ProductAvailabilityDto(int ProductId, bool Available, int? MaximumQuantity, InventoryControlMode? ControlMode, bool UsesFallback, IReadOnlyList<string> MissingItems);
public sealed record InventoryBalanceDto(int ExpenseId, string ExpenseName, InventoryBaseUnit BaseUnit, decimal QuantityOnHand, decimal QuantityReserved, decimal QuantityAvailable, decimal AverageUnitCost, decimal InventoryValue);
public sealed record InventoryMovementDto(int Id, int ExpenseId, string ExpenseName, InventoryMovementType Type, decimal OnHandDelta, decimal ReservedDelta, decimal UnitCost, int? OrderId, int? ExpenseHeaderId, int? TransferId, int? InventoryCountId, string OperationKey, string? Reason, DateTime CreatedAt);
public sealed record InventoryRequirementInput(int ExpenseId, decimal BaseQuantity);
public sealed record InventoryConversionInput(string Name, decimal BaseQuantity, bool Active = true);
public sealed record InventoryCountLineInput(int ExpenseId, decimal CountedQuantity);
public sealed record InventoryCountDraftLineInput(int ExpenseId, decimal? CountedQuantity);
public sealed record CopyInventoryConfigurationRequest(int SourceExpenseId, IReadOnlyList<int> TargetExpenseIds);
public sealed record CopyInventoryConfigurationResult(int SourceExpenseId, IReadOnlyList<int> UpdatedExpenseIds);
public sealed record InventoryTransferLineInput(int ExpenseId, decimal Quantity);
public sealed record ReceiveInventoryTransferLineInput(int ExpenseId, decimal ReceivedQuantity);
public sealed record InventoryTransferInput(int SourceBranchId, int DestinationBranchId, IReadOnlyList<InventoryTransferLineInput> Lines);
public sealed record ReceiveInventoryTransferInput(IReadOnlyList<ReceiveInventoryTransferLineInput> Lines, string? DifferenceReason);
public sealed record InventoryAdjustmentInput(int ExpenseId, decimal QuantityDelta, string Reason, bool Waste = false);
public sealed record InventoryRecipeDto(int ProductId, bool InventoryEnabled, InventoryControlMode ControlMode, IReadOnlyList<InventoryRequirementDto> Requirements);
public sealed record InventoryRequirementDto(int ExpenseId, string ExpenseName, InventoryBaseUnit BaseUnit, decimal BaseQuantity);
public sealed record InventoryCountDto(int Id, int BranchId, InventoryCountStatus Status, DateTime CreatedAt, DateTime? ConfirmedAt, IReadOnlyList<InventoryCountLineDto> Lines);
public sealed record InventoryCountLineDto(int ExpenseId, string ExpenseName, InventoryBaseUnit BaseUnit, decimal ExpectedQuantity, decimal? CountedQuantity, decimal? Difference);
public sealed record InventoryTransferDto(int Id, int SourceBranchId, string SourceBranchName, int DestinationBranchId, string DestinationBranchName, InventoryTransferStatus Status, DateTime CreatedAt, DateTime? DispatchedAt, DateTime? ReceivedAt, string? DifferenceReason, IReadOnlyList<InventoryTransferLineDto> Lines);
public sealed record InventoryTransferLineDto(int ExpenseId, string ExpenseName, InventoryBaseUnit BaseUnit, decimal Quantity, decimal? ReceivedQuantity, decimal UnitCost);
public sealed record InventoryReportRowDto(int ExpenseId, string ExpenseName, InventoryBaseUnit BaseUnit, decimal Purchases, decimal EstimatedConsumption, decimal StrictConsumption, decimal Adjustments, decimal Waste, decimal TransferIn, decimal TransferOut, decimal? LatestExpected, decimal? LatestCounted, decimal? LatestDifference, decimal? LatestDifferencePercentage, DateTime? LatestCountedAt);
public sealed record InventoryDeviationPointDto(int CountId, int ExpenseId, string ExpenseName, decimal ExpectedQuantity, decimal CountedQuantity, decimal Difference, decimal? DifferencePercentage, DateTime CountedAt);

