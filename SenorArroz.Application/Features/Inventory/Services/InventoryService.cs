using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Inventory.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Inventory.Services;

public sealed class InventoryService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IClock clock,
    IBranchContext branchContext) : IInventoryService
{
    public async Task<IReadOnlyList<ProductAvailabilityDto>> GetAvailabilityAsync(IReadOnlyCollection<int> productIds, int branchId, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().Include(x => x.Category)
            .Where(x => productIds.Contains(x.Id)).ToListAsync(ct);
        var requirements = await db.ProductExpenseRequirements.AsNoTracking().Include(x => x.Expense)
            .Where(x => productIds.Contains(x.ProductId)).ToListAsync(ct);
        var expenseIds = requirements.Select(x => x.ExpenseId).Distinct().ToArray();
        var balances = await db.InventoryBalances.AsNoTracking()
            .Where(x => x.BranchId == branchId && expenseIds.Contains(x.ExpenseId))
            .ToDictionaryAsync(x => x.ExpenseId, ct);

        return products.Select(product =>
        {
            if (!product.InventoryEnabled)
            {
                var unlimitedRice = product.Category.Name.Contains("arroz", StringComparison.OrdinalIgnoreCase);
                var available = product.Active && (unlimitedRice || !product.Stock.HasValue || product.Stock > 0);
                return new ProductAvailabilityDto(product.Id, available, unlimitedRice || !product.Stock.HasValue ? null : product.Stock, null, true, []);
            }
            if (product.InventoryControlMode == InventoryControlMode.Estimated)
                return new ProductAvailabilityDto(product.Id, product.Active, null, InventoryControlMode.Estimated, false, []);

            var recipe = requirements.Where(x => x.ProductId == product.Id).ToArray();
            var missing = recipe.Where(x => !balances.TryGetValue(x.ExpenseId, out var balance) || balance.QuantityOnHand - balance.QuantityReserved < x.BaseQuantity)
                .Select(x => x.Expense.Name).Distinct().ToArray();
            var maximum = recipe.Length == 0 ? 0 : recipe.Min(x => balances.TryGetValue(x.ExpenseId, out var balance)
                ? (int)Math.Floor(Math.Max(0, balance.QuantityOnHand - balance.QuantityReserved) / x.BaseQuantity)
                : 0);
            return new ProductAvailabilityDto(product.Id, product.Active && maximum > 0, maximum, InventoryControlMode.Strict, false, missing);
        }).ToArray();
    }

    public Task SnapshotAndReserveAsync(Order order, bool deferStrictReservation, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => SnapshotAndReserveCoreAsync(order, deferStrictReservation, operationKey, ct), ct);

    private async Task SnapshotAndReserveCoreAsync(Order order, bool deferStrictReservation, string operationKey, CancellationToken ct)
    {
        if (await db.InventoryMovements.AnyAsync(x => x.OperationKey == operationKey, ct)) return;
        if (!await db.OrderInventoryAllocations.AnyAsync(x => x.OrderId == order.Id, ct))
        {
            var details = order.OrderDetails.Count > 0 ? order.OrderDetails.ToArray() : await db.OrderDetails.Where(x => x.OrderId == order.Id).ToArrayAsync(ct);
            var productIds = details.Select(x => x.ProductId).Distinct().ToArray();
            var products = await db.Products.Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var requirements = await db.ProductExpenseRequirements.Where(x => productIds.Contains(x.ProductId)).ToArrayAsync(ct);
            foreach (var detail in details.GroupBy(x => x.ProductId).Select(x => new { ProductId = x.Key, Quantity = x.Sum(d => d.Quantity) }))
            {
                if (!products.TryGetValue(detail.ProductId, out var product) || !product.InventoryEnabled) continue;
                var recipe = requirements.Where(x => x.ProductId == product.Id).ToArray();
                if (recipe.Length == 0) throw new BusinessException($"El producto {product.Name} tiene inventario activo sin receta");
                foreach (var requirement in recipe)
                {
                    db.OrderInventoryAllocations.Add(new OrderInventoryAllocation
                    {
                        OrderId = order.Id,
                        ProductId = product.Id,
                        ExpenseId = requirement.ExpenseId,
                        ControlMode = product.InventoryControlMode,
                        BaseQuantity = requirement.BaseQuantity * detail.Quantity
                    });
                }
            }
            await db.SaveChangesAsync(ct);
        }
        if (!deferStrictReservation) await ReserveStrictAsync(order, operationKey, ct);
    }

    public Task ConsumeOrderAsync(Order order, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => ConsumeOrderCoreAsync(order, operationKey, ct), ct);

    private async Task ConsumeOrderCoreAsync(Order order, string operationKey, CancellationToken ct)
    {
        if (await db.InventoryMovements.AnyAsync(x => x.OperationKey == operationKey, ct)) return;
        var allocations = await db.OrderInventoryAllocations.Where(x => x.OrderId == order.Id && x.ConsumedQuantity == 0).ToArrayAsync(ct);
        if (allocations.Length == 0)
        {
            await SnapshotAndReserveAsync(order, false, $"{operationKey}:snapshot", ct);
            allocations = await db.OrderInventoryAllocations.Where(x => x.OrderId == order.Id && x.ConsumedQuantity == 0).ToArrayAsync(ct);
        }
        var grouped = allocations.GroupBy(x => new { x.ExpenseId, x.ControlMode }).OrderBy(x => x.Key.ExpenseId);
        foreach (var group in grouped)
        {
            var quantity = group.Sum(x => x.BaseQuantity);
            var balance = await LockBalanceAsync(order.BranchId, group.Key.ExpenseId, ct);
            if (group.Key.ControlMode == InventoryControlMode.Strict)
            {
                var reserved = group.Sum(x => x.ReservedQuantity);
                if (reserved < quantity || balance.QuantityReserved < quantity || balance.QuantityOnHand < quantity)
                    throw new BusinessException("El pedido no tiene inventario estricto reservado suficiente");
                balance.QuantityReserved -= quantity;
                balance.QuantityOnHand -= quantity;
                AddMovement(order.BranchId, group.Key.ExpenseId, InventoryMovementType.StrictConsumption, -quantity, -quantity, balance.AverageUnitCost, operationKey, order.Id, createdById: order.TakenById);
            }
            else
            {
                balance.QuantityOnHand -= quantity;
                AddMovement(order.BranchId, group.Key.ExpenseId, InventoryMovementType.EstimatedConsumption, -quantity, 0, balance.AverageUnitCost, operationKey, order.Id, createdById: order.TakenById);
            }
            foreach (var allocation in group) allocation.ConsumedQuantity = allocation.BaseQuantity;
        }
        order.InventoryIssue = null;
        await db.SaveChangesAsync(ct);
    }

    public Task ReleaseOrderAsync(Order order, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => ReleaseOrderCoreAsync(order, operationKey, ct), ct);

    private async Task ReleaseOrderCoreAsync(Order order, string operationKey, CancellationToken ct)
    {
        if (await db.InventoryMovements.AnyAsync(x => x.OperationKey == operationKey, ct)) return;
        var allocations = await db.OrderInventoryAllocations.Where(x => x.OrderId == order.Id && x.ReservedQuantity > 0 && x.ConsumedQuantity == 0).ToArrayAsync(ct);
        foreach (var group in allocations.GroupBy(x => x.ExpenseId).OrderBy(x => x.Key))
        {
            var quantity = group.Sum(x => x.ReservedQuantity);
            var balance = await LockBalanceAsync(order.BranchId, group.Key, ct);
            balance.QuantityReserved -= quantity;
            AddMovement(order.BranchId, group.Key, InventoryMovementType.ReservationRelease, 0, -quantity, balance.AverageUnitCost, operationKey, order.Id, createdById: order.TakenById);
            foreach (var allocation in group) allocation.ReservedQuantity = 0;
        }
        await db.SaveChangesAsync(ct);
    }

    public Task RecordPurchaseAsync(ExpenseHeader header, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => RecordPurchaseCoreAsync(header, operationKey, ct), ct);

    private async Task RecordPurchaseCoreAsync(ExpenseHeader header, string operationKey, CancellationToken ct)
    {
        if (await db.InventoryMovements.AnyAsync(x => x.OperationKey == operationKey, ct)) return;
        var expenseIds = header.ExpenseDetails.Select(x => x.ExpenseId).Distinct().ToArray();
        var expenses = await db.Expenses.Where(x => expenseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var conversionIds = header.ExpenseDetails.Where(x => x.InventoryConversionId.HasValue).Select(x => x.InventoryConversionId!.Value).Distinct().ToArray();
        var conversions = await db.ExpenseUnitConversions.Where(x => conversionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var purchases = new List<(ExpenseDetail Detail, decimal BaseQuantity, decimal Gross, decimal UnitCost)>();
        foreach (var detail in header.ExpenseDetails)
        {
            var expense = expenses[detail.ExpenseId];
            if (!expense.TracksInventory) continue;
            if (!expense.InventoryActive) throw new BusinessException($"El inventario de {expense.Name} no está activo");
            if (!detail.InventoryConversionId.HasValue || !conversions.TryGetValue(detail.InventoryConversionId.Value, out var conversion) || conversion.ExpenseId != detail.ExpenseId || !conversion.Active)
                throw new BusinessException($"Selecciona una presentación de compra válida para {expense.Name}");
            var baseQuantity = detail.Quantity * conversion.BaseQuantity;
            if (baseQuantity <= 0) throw new BusinessException($"La cantidad comprada de {expense.Name} debe ser mayor que cero");
            var net = detail.Total ?? detail.Quantity * detail.Amount;
            var gross = detail.IncludeVat ? net * 1.19m : net;
            var unitCost = gross / baseQuantity;
            detail.InventoryBaseQuantity = baseQuantity;
            detail.InventoryUnitCost = unitCost;
            purchases.Add((detail, baseQuantity, gross, unitCost));
        }
        foreach (var purchase in purchases.GroupBy(x => x.Detail.ExpenseId).OrderBy(x => x.Key))
        {
            var baseQuantity = purchase.Sum(x => x.BaseQuantity);
            var gross = purchase.Sum(x => x.Gross);
            var balance = await LockBalanceAsync(header.BranchId, purchase.Key, ct);
            var positiveCurrentQuantity = Math.Max(balance.QuantityOnHand, 0);
            var previousValue = positiveCurrentQuantity * balance.AverageUnitCost;
            balance.QuantityOnHand += baseQuantity;
            balance.AverageUnitCost = (previousValue + gross) / (positiveCurrentQuantity + baseQuantity);
            AddMovement(header.BranchId, purchase.Key, InventoryMovementType.Purchase, baseQuantity, 0, gross / baseQuantity, operationKey, createdById: header.CreatedById);
        }
        await db.SaveChangesAsync(ct);
    }

    public Task ReversePurchaseAsync(ExpenseHeader header, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => ReversePurchaseCoreAsync(header, operationKey, ct), ct);

    private async Task ReversePurchaseCoreAsync(ExpenseHeader header, string operationKey, CancellationToken ct)
    {
        if (await db.InventoryMovements.AnyAsync(x => x.OperationKey == operationKey, ct)) return;
        var strictExpenseIds = await db.ProductExpenseRequirements.Where(x => x.Product.InventoryEnabled && x.Product.InventoryControlMode == InventoryControlMode.Strict)
            .Select(x => x.ExpenseId).Distinct().ToArrayAsync(ct);
        foreach (var details in header.ExpenseDetails.Where(x => x.InventoryBaseQuantity > 0).GroupBy(x => x.ExpenseId).OrderBy(x => x.Key))
        {
            var quantity = details.Sum(x => x.InventoryBaseQuantity!.Value);
            var totalValue = details.Sum(x => x.InventoryBaseQuantity!.Value * (x.InventoryUnitCost ?? 0));
            var balance = await LockBalanceAsync(header.BranchId, details.Key, ct);
            if (strictExpenseIds.Contains(details.Key) && balance.QuantityOnHand - quantity < balance.QuantityReserved)
                throw new BusinessException("No se puede revertir la compra porque compromete inventario estricto reservado");
            var currentValue = Math.Max(balance.QuantityOnHand, 0) * balance.AverageUnitCost;
            balance.QuantityOnHand -= quantity;
            if (balance.QuantityOnHand > 0)
                balance.AverageUnitCost = Math.Max(currentValue - totalValue, 0) / balance.QuantityOnHand;
            else if (balance.QuantityOnHand == 0)
                balance.AverageUnitCost = 0;
            AddMovement(header.BranchId, details.Key, InventoryMovementType.Reversal, -quantity, 0, totalValue > 0 ? totalValue / quantity : balance.AverageUnitCost, operationKey, createdById: header.CreatedById);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<InventoryBalanceDto>> GetBalancesAsync(int branchId, CancellationToken ct)
        => await db.InventoryBalances.AsNoTracking().Where(x => x.BranchId == branchId).OrderBy(x => x.Expense.Name)
            .Select(x => new InventoryBalanceDto(x.ExpenseId, x.Expense.Name, x.Expense.InventoryBaseUnit, x.QuantityOnHand, x.QuantityReserved, x.QuantityOnHand - x.QuantityReserved, x.AverageUnitCost, Math.Max(x.QuantityOnHand, 0) * x.AverageUnitCost))
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<InventoryMovementDto>> GetMovementsAsync(int branchId, int take, CancellationToken ct)
        => await db.InventoryMovements.AsNoTracking().Where(x => x.BranchId == branchId).OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(take, 1, 500))
            .Select(x => new InventoryMovementDto(x.Id, x.ExpenseId, x.Expense.Name, x.Type, x.OnHandDelta, x.ReservedDelta, x.UnitCost, x.OrderId, x.OperationKey, x.Reason, x.CreatedAt))
            .ToArrayAsync(ct);

    public async Task<InventoryRecipeDto> GetRecipeAsync(int productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, ct)
            ?? throw new NotFoundException("Producto no encontrado");
        var requirements = await db.ProductExpenseRequirements.AsNoTracking()
            .Where(x => x.ProductId == productId)
            .OrderBy(x => x.Expense.Name)
            .Select(x => new InventoryRequirementDto(x.ExpenseId, x.Expense.Name, x.Expense.InventoryBaseUnit, x.BaseQuantity))
            .ToArrayAsync(ct);
        return new InventoryRecipeDto(product.Id, product.InventoryEnabled, product.InventoryControlMode, requirements);
    }

    public Task<InventoryRecipeDto> SetRecipeAsync(int productId, InventoryControlMode mode, bool enabled, IReadOnlyCollection<InventoryRequirementInput> requirements, CancellationToken ct)
        => ExecuteMutationAsync(() => SetRecipeCoreAsync(productId, mode, enabled, requirements, ct), ct);

    private async Task<InventoryRecipeDto> SetRecipeCoreAsync(int productId, InventoryControlMode mode, bool enabled, IReadOnlyCollection<InventoryRequirementInput> requirements, CancellationToken ct)
    {
        if (requirements.Count == 0) throw new BusinessException("La receta debe contener al menos un insumo");
        if (requirements.Any(x => x.BaseQuantity <= 0) || requirements.GroupBy(x => x.ExpenseId).Any(x => x.Count() > 1))
            throw new BusinessException("La receta contiene cantidades inválidas o insumos repetidos");
        var product = await db.Products.Include(x => x.Category).SingleOrDefaultAsync(x => x.Id == productId, ct) ?? throw new NotFoundException("Producto no encontrado");
        var expenseIds = requirements.Select(x => x.ExpenseId).ToArray();
        var expenses = await db.Expenses.Where(x => expenseIds.Contains(x.Id) && x.TracksInventory && x.InventoryActive).ToDictionaryAsync(x => x.Id, ct);
        if (expenses.Count != expenseIds.Length) throw new BusinessException("Todos los insumos de la receta deben tener inventario activo");
        if (enabled)
        {
            var countedExpenseIds = await db.InventoryCountLines
                .Where(x => x.InventoryCount.BranchId == product.Category.BranchId
                    && x.InventoryCount.Status == InventoryCountStatus.Confirmed
                    && expenseIds.Contains(x.ExpenseId))
                .Select(x => x.ExpenseId)
                .Distinct()
                .ToListAsync(ct);
            if (expenseIds.Except(countedExpenseIds).Any())
                throw new BusinessException("Confirma un conteo de apertura de todos los insumos antes de activar el inventario");
        }
        var current = await db.ProductExpenseRequirements.Where(x => x.ProductId == productId).ToArrayAsync(ct);
        db.ProductExpenseRequirements.RemoveRange(current);
        db.ProductExpenseRequirements.AddRange(requirements.Select(x => new ProductExpenseRequirement { ProductId = productId, ExpenseId = x.ExpenseId, BaseQuantity = x.BaseQuantity }));
        product.InventoryControlMode = mode;
        product.InventoryEnabled = enabled;
        await db.SaveChangesAsync(ct);
        return new InventoryRecipeDto(productId, enabled, mode, requirements.Select(x => new InventoryRequirementDto(x.ExpenseId, expenses[x.ExpenseId].Name, expenses[x.ExpenseId].InventoryBaseUnit, x.BaseQuantity)).ToArray());
    }

    public Task AdjustAsync(int branchId, InventoryAdjustmentInput input, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => AdjustCoreAsync(branchId, input, operationKey, ct), ct);

    private async Task AdjustCoreAsync(int branchId, InventoryAdjustmentInput input, string operationKey, CancellationToken ct)
    {
        if (input.QuantityDelta == 0 || string.IsNullOrWhiteSpace(input.Reason)) throw new BusinessException("La cantidad y el motivo del ajuste son obligatorios");
        if (await db.InventoryMovements.AnyAsync(x => x.OperationKey == operationKey, ct)) return;
        var expense = await db.Expenses.SingleOrDefaultAsync(x => x.Id == input.ExpenseId && x.TracksInventory && x.InventoryActive, ct) ?? throw new BusinessException("Insumo inventariable no encontrado");
        var balance = await LockBalanceAsync(branchId, input.ExpenseId, ct);
        var isStrict = await db.ProductExpenseRequirements.AnyAsync(x => x.ExpenseId == input.ExpenseId && x.Product.InventoryEnabled && x.Product.InventoryControlMode == InventoryControlMode.Strict, ct);
        if (input.QuantityDelta < 0 && isStrict && balance.QuantityOnHand + input.QuantityDelta < balance.QuantityReserved)
            throw new BusinessException("El ajuste dejaría inventario estricto por debajo de lo reservado");
        balance.QuantityOnHand += input.QuantityDelta;
        var type = input.Waste ? InventoryMovementType.Waste : input.QuantityDelta > 0 ? InventoryMovementType.AdjustmentIncrease : InventoryMovementType.AdjustmentDecrease;
        AddMovement(branchId, expense.Id, type, input.QuantityDelta, 0, balance.AverageUnitCost, operationKey, reason: input.Reason.Trim());
        await db.SaveChangesAsync(ct);
    }

    public Task<InventoryCountDto> StartCountAsync(int branchId, CancellationToken ct)
        => ExecuteMutationAsync(() => StartCountCoreAsync(branchId, ct), ct);

    private async Task<InventoryCountDto> StartCountCoreAsync(int branchId, CancellationToken ct)
    {
        if (await db.InventoryCounts.AnyAsync(x => x.BranchId == branchId && x.Status == InventoryCountStatus.Draft, ct)) throw new BusinessException("Ya existe un conteo abierto para la sucursal");
        var countLines = await db.Expenses.AsNoTracking().Where(x => x.TracksInventory && x.InventoryActive)
            .OrderBy(x => x.Name)
            .Select(expense => new InventoryCountLine
            {
                ExpenseId = expense.Id,
                ExpectedQuantity = db.InventoryBalances.Where(x => x.BranchId == branchId && x.ExpenseId == expense.Id)
                    .Select(x => x.QuantityOnHand).FirstOrDefault(),
                AverageUnitCost = db.InventoryBalances.Where(x => x.BranchId == branchId && x.ExpenseId == expense.Id)
                    .Select(x => x.AverageUnitCost).FirstOrDefault()
            }).ToListAsync(ct);
        if (countLines.Count == 0) throw new BusinessException("No hay insumos inventariables activos para contar");
        var count = new InventoryCount { BranchId = branchId, CreatedById = currentUser.Id, Lines = countLines };
        db.InventoryCounts.Add(count); await db.SaveChangesAsync(ct);
        return await MapCountAsync(count.Id, ct);
    }

    public async Task<IReadOnlyList<InventoryCountDto>> GetCountsAsync(int branchId, CancellationToken ct)
    {
        var ids = await db.InventoryCounts.AsNoTracking().Where(x => x.BranchId == branchId)
            .OrderByDescending(x => x.CreatedAt).Select(x => x.Id).Take(100).ToArrayAsync(ct);
        var result = new List<InventoryCountDto>(ids.Length);
        foreach (var id in ids) result.Add(await MapCountAsync(id, ct));
        return result;
    }

    public Task<InventoryCountDto> ConfirmCountAsync(int countId, IReadOnlyCollection<InventoryCountLineInput> lines, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => ConfirmCountCoreAsync(countId, lines, operationKey, ct), ct);

    private async Task<InventoryCountDto> ConfirmCountCoreAsync(int countId, IReadOnlyCollection<InventoryCountLineInput> lines, string operationKey, CancellationToken ct)
    {
        var count = await db.InventoryCounts.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == countId, ct) ?? throw new NotFoundException("Conteo no encontrado");
        branchContext.EnsureAccess(count.BranchId);
        if (count.Status == InventoryCountStatus.Confirmed) return await MapCountAsync(countId, ct);
        if (lines.Count != count.Lines.Count || lines.Select(x => x.ExpenseId).Distinct().Count() != count.Lines.Count) throw new BusinessException("Debes contar todos los insumos del conteo");
        foreach (var line in count.Lines.OrderBy(x => x.ExpenseId))
        {
            var input = lines.SingleOrDefault(x => x.ExpenseId == line.ExpenseId) ?? throw new BusinessException("Falta un insumo en el conteo");
            if (input.CountedQuantity < 0) throw new BusinessException("La cantidad física no puede ser negativa");
            var balance = await LockBalanceAsync(count.BranchId, line.ExpenseId, ct);
            if (input.CountedQuantity < balance.QuantityReserved) throw new BusinessException("El conteo físico no puede quedar por debajo de las unidades reservadas");
            var difference = input.CountedQuantity - balance.QuantityOnHand;
            line.CountedQuantity = input.CountedQuantity; line.Difference = difference;
            if (difference != 0)
            {
                var hasPreviousCount = await db.InventoryCountLines.AnyAsync(x => x.ExpenseId == line.ExpenseId
                    && x.InventoryCount.BranchId == count.BranchId
                    && x.InventoryCount.Status == InventoryCountStatus.Confirmed, ct);
                balance.QuantityOnHand = input.CountedQuantity;
                var movementType = !hasPreviousCount
                    ? InventoryMovementType.OpeningBalance
                    : difference > 0 ? InventoryMovementType.AdjustmentIncrease : InventoryMovementType.AdjustmentDecrease;
                AddMovement(count.BranchId, line.ExpenseId, movementType, difference, 0, balance.AverageUnitCost, operationKey, inventoryCountId: count.Id, reason: "Conteo físico");
            }
        }
        count.Status = InventoryCountStatus.Confirmed; count.ConfirmedById = currentUser.Id; count.ConfirmedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct); return await MapCountAsync(countId, ct);
    }

    public Task<InventoryTransferDto> CreateTransferAsync(InventoryTransferInput input, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => CreateTransferCoreAsync(input, operationKey, ct), ct);

    private async Task<InventoryTransferDto> CreateTransferCoreAsync(InventoryTransferInput input, string operationKey, CancellationToken ct)
    {
        branchContext.EnsureAccess(input.SourceBranchId);
        var existingId = await db.InventoryTransfers.AsNoTracking()
            .Where(x => x.OperationKey == operationKey)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (existingId.HasValue) return await MapTransferAsync(existingId.Value, ct);
        if (input.SourceBranchId == input.DestinationBranchId || input.Lines.Count == 0 || input.Lines.Any(x => x.Quantity <= 0) || input.Lines.GroupBy(x => x.ExpenseId).Any(x => x.Count() > 1)) throw new BusinessException("Transferencia inválida");
        var branches = await db.Branches.CountAsync(x => x.Id == input.SourceBranchId || x.Id == input.DestinationBranchId, ct);
        if (branches != 2) throw new BusinessException("Las sucursales no pertenecen al restaurante actual");
        var expenseIds = input.Lines.Select(x => x.ExpenseId).ToArray();
        if (await db.Expenses.CountAsync(x => expenseIds.Contains(x.Id) && x.TracksInventory && x.InventoryActive, ct) != expenseIds.Length) throw new BusinessException("La transferencia contiene insumos inválidos");
        var transfer = new InventoryTransfer { SourceBranchId = input.SourceBranchId, DestinationBranchId = input.DestinationBranchId, OperationKey = operationKey, CreatedById = currentUser.Id, Lines = input.Lines.Select(x => new InventoryTransferLine { ExpenseId = x.ExpenseId, Quantity = x.Quantity }).ToList() };
        db.InventoryTransfers.Add(transfer); await db.SaveChangesAsync(ct); return await MapTransferAsync(transfer.Id, ct);
    }

    public Task<InventoryTransferDto> DispatchTransferAsync(int transferId, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => DispatchTransferCoreAsync(transferId, operationKey, ct), ct);

    private async Task<InventoryTransferDto> DispatchTransferCoreAsync(int transferId, string operationKey, CancellationToken ct)
    {
        var transfer = await db.InventoryTransfers.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == transferId, ct) ?? throw new NotFoundException("Transferencia no encontrada");
        branchContext.EnsureAccess(transfer.SourceBranchId);
        if (transfer.Status != InventoryTransferStatus.Draft) return await MapTransferAsync(transferId, ct);
        foreach (var line in transfer.Lines.OrderBy(x => x.ExpenseId))
        {
            var balance = await LockBalanceAsync(transfer.SourceBranchId, line.ExpenseId, ct);
            if (balance.QuantityOnHand - balance.QuantityReserved < line.Quantity) throw new BusinessException("No hay existencia disponible suficiente para despachar");
            line.UnitCost = balance.AverageUnitCost; balance.QuantityOnHand -= line.Quantity;
            AddMovement(transfer.SourceBranchId, line.ExpenseId, InventoryMovementType.TransferOut, -line.Quantity, 0, line.UnitCost, operationKey, transferId: transfer.Id);
        }
        transfer.Status = InventoryTransferStatus.Dispatched; transfer.DispatchedById = currentUser.Id; transfer.DispatchedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct); return await MapTransferAsync(transferId, ct);
    }

    public Task<InventoryTransferDto> ReceiveTransferAsync(int transferId, ReceiveInventoryTransferInput input, string operationKey, CancellationToken ct)
        => ExecuteMutationAsync(() => ReceiveTransferCoreAsync(transferId, input, operationKey, ct), ct);

    private async Task<InventoryTransferDto> ReceiveTransferCoreAsync(int transferId, ReceiveInventoryTransferInput input, string operationKey, CancellationToken ct)
    {
        var transfer = await db.InventoryTransfers.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == transferId, ct) ?? throw new NotFoundException("Transferencia no encontrada");
        branchContext.EnsureAccess(transfer.DestinationBranchId);
        if (transfer.Status is InventoryTransferStatus.Received or InventoryTransferStatus.ReceivedWithDifference) return await MapTransferAsync(transferId, ct);
        if (transfer.Status != InventoryTransferStatus.Dispatched) throw new BusinessException("La transferencia aún no ha sido despachada");
        var hasDifference = false;
        foreach (var line in transfer.Lines.OrderBy(x => x.ExpenseId))
        {
            var received = input.Lines.SingleOrDefault(x => x.ExpenseId == line.ExpenseId)?.ReceivedQuantity ?? throw new BusinessException("Debes confirmar todos los insumos transferidos");
            if (received < 0 || received > line.Quantity) throw new BusinessException("La cantidad recibida debe estar entre cero y lo despachado");
            hasDifference |= received != line.Quantity; line.ReceivedQuantity = received;
            var balance = await LockBalanceAsync(transfer.DestinationBranchId, line.ExpenseId, ct);
            var currentPositive = Math.Max(balance.QuantityOnHand, 0); var currentValue = currentPositive * balance.AverageUnitCost;
            balance.QuantityOnHand += received;
            if (received > 0) balance.AverageUnitCost = (currentValue + received * line.UnitCost) / (currentPositive + received);
            AddMovement(transfer.DestinationBranchId, line.ExpenseId, InventoryMovementType.TransferIn, received, 0, line.UnitCost, operationKey, transferId: transfer.Id);
        }
        if (hasDifference && string.IsNullOrWhiteSpace(input.DifferenceReason)) throw new BusinessException("El motivo de la diferencia es obligatorio");
        transfer.Status = hasDifference ? InventoryTransferStatus.ReceivedWithDifference : InventoryTransferStatus.Received; transfer.DifferenceReason = hasDifference ? input.DifferenceReason!.Trim() : null; transfer.ReceivedById = currentUser.Id; transfer.ReceivedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct); return await MapTransferAsync(transferId, ct);
    }

    public async Task<IReadOnlyList<InventoryTransferDto>> GetTransfersAsync(int branchId, CancellationToken ct)
    {
        var ids = await db.InventoryTransfers.AsNoTracking().Where(x => x.SourceBranchId == branchId || x.DestinationBranchId == branchId).OrderByDescending(x => x.CreatedAt).Select(x => x.Id).Take(200).ToArrayAsync(ct);
        var result = new List<InventoryTransferDto>(ids.Length); foreach (var id in ids) result.Add(await MapTransferAsync(id, ct)); return result;
    }

    public async Task<IReadOnlyList<InventoryReportRowDto>> GetReportAsync(int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (toUtc <= fromUtc) throw new BusinessException("El rango del reporte no es válido");
        var movements = await db.InventoryMovements.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.CreatedAt >= fromUtc && x.CreatedAt < toUtc)
            .GroupBy(x => new { x.ExpenseId, x.Expense.Name, x.Expense.InventoryBaseUnit })
            .Select(x => new
            {
                x.Key.ExpenseId,
                ExpenseName = x.Key.Name,
                BaseUnit = x.Key.InventoryBaseUnit,
                Purchases = x.Where(m => m.Type == InventoryMovementType.Purchase || m.Type == InventoryMovementType.Reversal).Sum(m => m.OnHandDelta),
                Estimated = -x.Where(m => m.Type == InventoryMovementType.EstimatedConsumption).Sum(m => m.OnHandDelta),
                Strict = -x.Where(m => m.Type == InventoryMovementType.StrictConsumption).Sum(m => m.OnHandDelta),
                Adjustments = x.Where(m => m.Type == InventoryMovementType.AdjustmentIncrease || m.Type == InventoryMovementType.AdjustmentDecrease).Sum(m => m.OnHandDelta),
                Waste = -x.Where(m => m.Type == InventoryMovementType.Waste).Sum(m => m.OnHandDelta),
                TransferIn = x.Where(m => m.Type == InventoryMovementType.TransferIn).Sum(m => m.OnHandDelta),
                TransferOut = -x.Where(m => m.Type == InventoryMovementType.TransferOut).Sum(m => m.OnHandDelta)
            }).ToArrayAsync(ct);
        var latestCounts = await db.InventoryCountLines.AsNoTracking()
            .Where(x => x.InventoryCount.BranchId == branchId && x.InventoryCount.Status == InventoryCountStatus.Confirmed
                && x.InventoryCount.ConfirmedAt >= fromUtc && x.InventoryCount.ConfirmedAt < toUtc)
            .OrderByDescending(x => x.InventoryCount.ConfirmedAt)
            .Select(x => new { x.ExpenseId, x.ExpectedQuantity, x.CountedQuantity, x.Difference, x.InventoryCount.ConfirmedAt })
            .ToArrayAsync(ct);
        return movements.Select(row =>
        {
            var count = latestCounts.FirstOrDefault(x => x.ExpenseId == row.ExpenseId);
            decimal? percentage = count?.Difference is decimal difference && count.ExpectedQuantity != 0
                ? difference / Math.Abs(count.ExpectedQuantity) * 100m
                : null;
            return new InventoryReportRowDto(row.ExpenseId, row.ExpenseName, row.BaseUnit, row.Purchases, row.Estimated,
                row.Strict, row.Adjustments, row.Waste, row.TransferIn, row.TransferOut, count?.ExpectedQuantity,
                count?.CountedQuantity, count?.Difference, percentage, count?.ConfirmedAt);
        }).OrderBy(x => x.ExpenseName).ToArray();
    }

    public async Task<IReadOnlyList<InventoryDeviationPointDto>> GetDeviationAsync(int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        => await db.InventoryCountLines.AsNoTracking()
            .Where(x => x.InventoryCount.BranchId == branchId && x.InventoryCount.Status == InventoryCountStatus.Confirmed
                && x.InventoryCount.ConfirmedAt >= fromUtc && x.InventoryCount.ConfirmedAt < toUtc && x.CountedQuantity.HasValue && x.Difference.HasValue)
            .OrderBy(x => x.InventoryCount.ConfirmedAt).ThenBy(x => x.Expense.Name)
            .Select(x => new InventoryDeviationPointDto(x.InventoryCountId, x.ExpenseId, x.Expense.Name, x.ExpectedQuantity,
                x.CountedQuantity!.Value, x.Difference!.Value,
                x.ExpectedQuantity == 0 ? null : x.Difference.Value / Math.Abs(x.ExpectedQuantity) * 100m,
                x.InventoryCount.ConfirmedAt!.Value))
            .ToArrayAsync(ct);

    private async Task<InventoryCountDto> MapCountAsync(int id, CancellationToken ct)
        => await db.InventoryCounts.AsNoTracking().Where(x => x.Id == id).Select(x => new InventoryCountDto(x.Id, x.BranchId, x.Status, x.CreatedAt, x.ConfirmedAt, x.Lines.OrderBy(l => l.Expense.Name).Select(l => new InventoryCountLineDto(l.ExpenseId, l.Expense.Name, l.Expense.InventoryBaseUnit, l.ExpectedQuantity, l.CountedQuantity, l.Difference)).ToArray())).SingleAsync(ct);

    private async Task<InventoryTransferDto> MapTransferAsync(int id, CancellationToken ct)
        => await db.InventoryTransfers.AsNoTracking().Where(x => x.Id == id).Select(x => new InventoryTransferDto(x.Id, x.SourceBranchId, x.SourceBranch.Name, x.DestinationBranchId, x.DestinationBranch.Name, x.Status, x.CreatedAt, x.DispatchedAt, x.ReceivedAt, x.DifferenceReason, x.Lines.OrderBy(l => l.Expense.Name).Select(l => new InventoryTransferLineDto(l.ExpenseId, l.Expense.Name, l.Expense.InventoryBaseUnit, l.Quantity, l.ReceivedQuantity, l.UnitCost)).ToArray())).SingleAsync(ct);

    private async Task ReserveStrictAsync(Order order, string operationKey, CancellationToken ct)
    {
        var allocations = await db.OrderInventoryAllocations.Where(x => x.OrderId == order.Id && x.ControlMode == InventoryControlMode.Strict && x.ReservedQuantity == 0 && x.ConsumedQuantity == 0).ToArrayAsync(ct);
        foreach (var group in allocations.GroupBy(x => x.ExpenseId).OrderBy(x => x.Key))
        {
            var quantity = group.Sum(x => x.BaseQuantity);
            var balance = await LockBalanceAsync(order.BranchId, group.Key, ct);
            if (balance.QuantityOnHand - balance.QuantityReserved < quantity)
                throw new BusinessException("No hay inventario suficiente para confirmar una o más bebidas");
            balance.QuantityReserved += quantity;
            AddMovement(order.BranchId, group.Key, InventoryMovementType.Reservation, 0, quantity, balance.AverageUnitCost, operationKey, order.Id, createdById: order.TakenById);
            foreach (var allocation in group) allocation.ReservedQuantity = allocation.BaseQuantity;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<InventoryBalance> LockBalanceAsync(int branchId, int expenseId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO inventory_balance (tenant_id, branch_id, expense_id, quantity_on_hand, quantity_reserved, average_unit_cost, created_at, updated_at) VALUES ({currentTenant.TenantId}, {branchId}, {expenseId}, 0, 0, 0, now(), now()) ON CONFLICT (tenant_id, branch_id, expense_id) DO NOTHING", ct);
            return await db.InventoryBalances.FromSqlInterpolated($"SELECT * FROM inventory_balance WHERE tenant_id = {currentTenant.TenantId} AND branch_id = {branchId} AND expense_id = {expenseId} FOR UPDATE").SingleAsync(ct);
        }
        var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.BranchId == branchId && x.ExpenseId == expenseId, ct);
        if (balance is not null) return balance;
        balance = new InventoryBalance { BranchId = branchId, ExpenseId = expenseId };
        db.InventoryBalances.Add(balance);
        await db.SaveChangesAsync(ct);
        return balance;
    }

    private async Task ExecuteMutationAsync(Func<Task> action, CancellationToken ct)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
        {
            await action();
            return;
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await action();
        await transaction.CommitAsync(ct);
    }

    private async Task<T> ExecuteMutationAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
            return await action();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }

    private void AddMovement(int branchId, int expenseId, InventoryMovementType type, decimal onHand, decimal reserved, decimal unitCost, string key, int? orderId = null, int? expenseDetailId = null, int? transferId = null, int? inventoryCountId = null, string? reason = null, int? createdById = null)
        => db.InventoryMovements.Add(new InventoryMovement { BranchId = branchId, ExpenseId = expenseId, Type = type, OnHandDelta = onHand, ReservedDelta = reserved, UnitCost = unitCost, OrderId = orderId, ExpenseDetailId = expenseDetailId, TransferId = transferId, InventoryCountId = inventoryCountId, CreatedById = createdById ?? currentUser.Id, OperationKey = key, Reason = reason, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow });
}
