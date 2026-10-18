using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Application.Features.Orders.Commands;

/// <summary>
/// Transfers a not-yet-prepared order, preserving its ID, totals, origin and history.
/// OrderMutationBehavior owns the lock, transaction, version and audit.
/// </summary>
public sealed class TransferOrderBranchHandler(
    IApplicationDbContext db,
    IOrderRepository orders,
    ICurrentUser currentUser,
    IBranchContext branchContext,
    IOrderMutationContext mutation,
    IInventoryService inventory,
    IOrderNotificationService notifications,
    IMapper mapper,
    IClock clock) : IRequestHandler<TransferOrderBranchCommand, OrderDto>
{
    public async Task<OrderDto> Handle(TransferOrderBranchCommand request, CancellationToken ct)
    {
        if (!Roles.IsSuperadmin(currentUser.Role))
            throw new BranchAccessDeniedException();

        // A branch transfer always requires a versioned snapshot.
        if (mutation.Expected is not { } expected || expected.OrderId != request.Id)
            throw new OrderConcurrencyException();

        var order = await db.Orders.Include(o => o.OrderDetails)
            .SingleOrDefaultAsync(o => o.Id == request.Id && o.TenantId == currentUser.TenantId, ct)
            ?? throw new NotFoundException("Pedido no encontrado");
        OrderAccessRules.EnsureAccess(order, currentUser, branchContext);

        var previousBranchId = order.BranchId;
        if (request.TargetBranchId <= 0 || previousBranchId == request.TargetBranchId)
            throw new BusinessException("Selecciona una sucursal de destino diferente.");

        var destination = await db.Branches.AsNoTracking()
            .Where(b => b.Id == request.TargetBranchId && b.TenantId == order.TenantId && b.IsActive)
            .Select(b => new { b.Id, b.Name }).SingleOrDefaultAsync(ct);
        if (destination is null)
            throw new BusinessException("La sucursal de destino no existe, no está activa o no pertenece al restaurante.");

        if (order.Status != OrderStatus.Taken || order.DeliveryManId.HasValue || order.DeliveryRouteId.HasValue)
            throw new BusinessException("Solo se pueden trasladar pedidos tomados, antes de preparación y sin domiciliario o ruta.");
        if (order.OrderDetails.Count == 0)
            throw new BusinessException("No se puede trasladar un pedido sin productos.");
        if (order.ExternalFulfillmentProvider is not null || order.DeliveryAppConnectionId.HasValue
            || order.ExternalOrderId is not null || order.StorefrontIdempotencyKey is not null)
            throw new BusinessException("Los pedidos vinculados con plataformas o pagos web no pueden trasladarse.");
        if (order.PaidInStoreCash || order.PaidInStoreCashAmount is > 0
            || await db.BankPayments.AnyAsync(p => p.OrderId == order.Id, ct)
            || await db.AppPayments.AnyAsync(p => p.OrderId == order.Id, ct)
            || await db.ReservationDeposits.AnyAsync(p => p.OrderId == order.Id, ct)
            || await db.WompiPaymentAttempts.AnyAsync(p => p.OrderId == order.Id, ct))
            throw new BusinessException("El pedido tiene cobros, abonos o pagos asociados. No se traslada para evitar inconsistencias contables.");
        if (order.LoyaltyCycleStepId.HasValue || order.ManualBenefitGiftProductId.HasValue)
            throw new BusinessException("El pedido tiene un premio de fidelidad o regalo ligado a la sucursal actual.");
        if (await db.OrderInventoryAllocations.AnyAsync(a => a.OrderId == order.Id && a.ConsumedQuantity > 0, ct))
            throw new BusinessException("Ya se consumió inventario del pedido en la sucursal de origen.");

        if (order.AddressId.HasValue && !await db.AddressBranches.AsNoTracking().AnyAsync(a =>
                a.AddressId == order.AddressId && a.BranchId == destination.Id
                && a.TenantId == order.TenantId && a.IsCovered, ct))
            throw new BusinessException("La sucursal de destino no tiene cobertura para la dirección del pedido.");
        if (order.Type == OrderType.Onsite && order.OriginBranchId != destination.Id
            && string.IsNullOrWhiteSpace(order.GuestName))
            throw new BusinessException("La recogida entre sucursales requiere el nombre de quien recoge.");

        // Catalog products are branch-owned: remap IDs without replacing order lines.
        var productIds = order.OrderDetails.Select(d => d.ProductId).Distinct().ToArray();
        var sourceProducts = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, CategoryName = p.Category.Name, BranchId = p.Category.BranchId })
            .ToListAsync(ct);
        if (sourceProducts.Count != productIds.Length
            || sourceProducts.Any(p => p.BranchId != previousBranchId))
            throw new BusinessException("Los productos originales no pertenecen a la sucursal de origen.");

        var targetProducts = await db.Products.AsNoTracking()
            .Where(p => p.Category.BranchId == destination.Id && p.Active)
            .Select(p => new { p.Id, p.Name, CategoryName = p.Category.Name }).ToListAsync(ct);
        var targetByName = targetProducts.ToLookup(
            p => ProductKey(p.CategoryName, p.Name), StringComparer.OrdinalIgnoreCase);
        var targetProductIds = new Dictionary<int, int>();
        foreach (var source in sourceProducts)
        {
            var matches = targetByName[ProductKey(source.CategoryName, source.Name)].ToArray();
            if (matches.Length != 1)
                throw new BusinessException(
                    $"El producto '{source.Name}' ({source.CategoryName}) no tiene una equivalencia única y activa en {destination.Name}.");
            targetProductIds[source.Id] = matches[0].Id;
        }

        var beforeKitchen = KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(order, clock.UtcNow);
        var operationKey = $"order:{order.Id}:transfer:{clock.UtcNow.Ticks}";

        // All writes run inside the behavior transaction, including inventory.
        await inventory.ReleaseOrderAsync(order, $"{operationKey}:release", ct);
        var allocations = await db.OrderInventoryAllocations.Where(a => a.OrderId == order.Id).ToListAsync(ct);
        db.OrderInventoryAllocations.RemoveRange(allocations);
        foreach (var detail in order.OrderDetails)
            detail.ProductId = targetProductIds[detail.ProductId];
        order.BranchId = destination.Id;
        order.InventoryIssue = null;
        db.KitchenVoiceEvents.RemoveRange(
            await db.KitchenVoiceEvents.Where(e => e.OrderId == order.Id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        var deferReservation = order.Type == OrderType.Reservation && order.PrepareAt > clock.UtcNow;
        await inventory.SnapshotAndReserveAsync(order, deferReservation, $"{operationKey}:reserve", ct);

        var persisted = await orders.GetByIdWithFullDetailsAsync(order.Id, ct)
            ?? throw new NotFoundException("Pedido no encontrado");
        var result = mapper.Map<OrderDto>(persisted);

        if (beforeKitchen)
            await OrderPostCommitNotifications.Publish(mutation,
                () => notifications.NotifyOrderCancelledToKitchen(previousBranchId, order.Id,
                    $"Trasladado a {destination.Name}"));

        if (KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(order, clock.UtcNow))
            await OrderPostCommitNotifications.Publish(mutation,
                () => notifications.NotifyNewOrderToKitchen(result));

        return result;
    }

    private static string ProductKey(string category, string name) =>
        $"{category.Trim()}|{name.Trim()}";
}
