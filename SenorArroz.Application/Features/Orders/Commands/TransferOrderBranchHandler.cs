using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Application.Features.Orders.Commands;

/// <summary>
/// Changes only the dispatch branch; existing inventory, assignments, payments,
/// products and history keep their original references. The mutation pipeline
/// owns the transaction, concurrency lock, version and audit entry.
/// </summary>
public sealed class TransferOrderBranchHandler(
    IApplicationDbContext db,
    IOrderRepository orders,
    ICurrentUser currentUser,
    IBranchContext branchContext,
    IOrderMutationContext mutation,
    IOrderNotificationService notifications,
    IMapper mapper,
    IClock clock) : IRequestHandler<TransferOrderBranchCommand, OrderDto>
{
    public async Task<OrderDto> Handle(TransferOrderBranchCommand request, CancellationToken ct)
    {
        if (!Roles.IsSuperadmin(currentUser.Role))
            throw new BranchAccessDeniedException();

        // Avoid overwriting a newer status, payment or assignment.
        if (mutation.Expected is not { } expected || expected.OrderId != request.Id)
            throw new OrderConcurrencyException();

        var order = await db.Orders.SingleOrDefaultAsync(
            o => o.Id == request.Id && o.TenantId == currentUser.TenantId, ct)
            ?? throw new NotFoundException("Pedido no encontrado");
        OrderAccessRules.EnsureAccess(order, currentUser, branchContext);

        var previousBranchId = order.BranchId;
        if (request.TargetBranchId <= 0 || request.TargetBranchId == previousBranchId)
            throw new BusinessException("Selecciona otra sucursal.");

        var targetExists = await db.Branches.AsNoTracking().AnyAsync(
            b => b.Id == request.TargetBranchId && b.TenantId == order.TenantId && b.IsActive, ct);
        if (!targetExists)
            throw new BusinessException("La sucursal de destino no existe o está inactiva.");

        // Keep the technical voice inbox intact: the runtime DB role has no DELETE
        // permission on it. The event stream filters out old-branch entries on read.
        var visibleToKitchen = KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(order, clock.UtcNow);
        order.BranchId = request.TargetBranchId;
        await db.SaveChangesAsync(ct);

        var persisted = await orders.GetByIdWithFullDetailsAsync(order.Id, ct)
            ?? throw new NotFoundException("Pedido no encontrado");
        var result = mapper.Map<OrderDto>(persisted);

        await OrderPostCommitNotifications.Publish(mutation,
            () => notifications.NotifyOrderTransferred(order.Id, previousBranchId,
                request.TargetBranchId, visibleToKitchen));

        return result;
    }
}
