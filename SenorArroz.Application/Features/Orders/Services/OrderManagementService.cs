using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Orders.Services;

/// <summary>Resource-scoped reads. A remote order never grants access to the branch's financial overview or GPS history.</summary>
public sealed class OrderManagementService(IApplicationDbContext db, ICurrentUser user, IBranchContext branch, IClock clock)
{
    public async Task<Order> RequireOrder(int orderId, CancellationToken ct)
    {
        if (!OrderAccessRules.IsManagementRole(user.Role)) throw new BranchAccessDeniedException();
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException("Pedido no encontrado");
        OrderAccessRules.EnsureAccess(order, user, branch);
        return order;
    }

    public async Task<List<RemoteOrderBranchDto>> GetRemoteBranches(CancellationToken ct)
    {
        if (!OrderAccessRules.IsManagementRole(user.Role)) throw new BranchAccessDeniedException();
        var origin = branch.RequireBranch();
        return await db.Orders.AsNoTracking()
            .Where(o => o.OriginBranchId == origin && o.BranchId != origin)
            .GroupBy(o => new { o.BranchId, o.Branch.Name })
            .OrderBy(g => g.Key.Name)
            .Select(g => new RemoteOrderBranchDto(g.Key.BranchId, g.Key.Name, g.Min(o => o.Id)))
            .ToListAsync(ct);
    }

    public async Task<OrderOperationOptionsDto> GetOptions(int orderId, CancellationToken ct)
    {
        var order = await RequireOrder(orderId, ct);
        var branchName = await db.Branches.AsNoTracking().Where(b => b.Id == order.BranchId)
            .Select(b => b.Name).SingleAsync(ct);
        // No balances, user profiles, expenses or settlement totals are returned.
        var banks = await db.Banks.AsNoTracking().Where(b => b.BranchId == order.BranchId && b.Active
                && b.Type == BankType.Normal)
            .OrderBy(b => b.Id).Select(b => new OrderBankOptionDto(b.Id, b.Name, b.BranchId, b.Active, b.ImageUrl, b.Type))
            .ToListAsync(ct);
        var apps = await db.Apps.AsNoTracking().Where(a => a.Bank.BranchId == order.BranchId && a.Active && a.Bank.Active)
            .OrderBy(a => a.Name).Select(a => new OrderAppOptionDto(a.Id, a.Name, a.BankId, a.Bank.Name, a.Bank.BranchId, a.Active, a.ImageUrl))
            .ToListAsync(ct);
        var drivers = await db.Users.AsNoTracking().Where(u => u.BranchId == order.BranchId && u.Active && u.Role == UserRole.Deliveryman)
            .OrderBy(u => u.Name).Select(u => new OrderDeliverymanOptionDto(u.Id, u.Name)).ToListAsync(ct);
        return new(order.BranchId, branchName, banks, apps, drivers);
    }

    public async Task<OrderTrackingDto> GetTracking(int orderId, CancellationToken ct)
    {
        var order = await RequireOrder(orderId, ct);
        OrderDriverLocationDto? location = null;
        string? driverName = null;
        if (order.DeliveryManId is { } driverId)
            driverName = await db.Users.AsNoTracking().Where(u => u.Id == driverId && u.BranchId == order.BranchId)
                .Select(u => u.Name).SingleOrDefaultAsync(ct);
        if (order.Type == OrderType.Delivery && order.Status == OrderStatus.OnTheWay
            && order.DeliveryManId.HasValue && order.DeliveryRouteId.HasValue && driverName is not null)
        {
            var now = clock.UtcNow;
            var activeRoute = await db.DeliveryRoutes.AsNoTracking().AnyAsync(r => r.Id == order.DeliveryRouteId
                && r.BranchId == order.BranchId && r.DeliverymanId == order.DeliveryManId && r.CompletedAtUtc == null
                && r.Status != DeliveryRouteStatus.Completed && r.Status != DeliveryRouteStatus.Cancelled, ct);
            if (activeRoute)
            {
                // Read the driver's latest point first; never substitute an old point from another route/session.
                var point = await db.DeliverymanLocations.AsNoTracking()
                    .Where(l => l.DeliverymanId == order.DeliveryManId)
                    .OrderByDescending(l => l.RecordedAt).ThenByDescending(l => l.Id)
                    .Select(l => new
                    {
                        l.Latitude, l.Longitude, l.RecordedAt, l.DeliveryRouteId,
                        SessionBranch = l.WorkSession == null ? (int?)null : l.WorkSession.BranchId,
                        SessionDriver = l.WorkSession == null ? (int?)null : l.WorkSession.DeliverymanId,
                        SessionEnd = l.WorkSession == null ? null : l.WorkSession.EndedAt,
                        SessionAutoClose = l.WorkSession == null ? (DateTime?)null : l.WorkSession.AutoCloseAt,
                        SessionStatus = l.WorkSession == null ? (DeliveryWorkSessionStatus?)null : l.WorkSession.Status
                    }).FirstOrDefaultAsync(ct);
                if (point is not null && point.DeliveryRouteId == order.DeliveryRouteId
                    && point.SessionBranch == order.BranchId && point.SessionDriver == order.DeliveryManId
                    && point.SessionStatus == DeliveryWorkSessionStatus.Active && point.SessionEnd == null
                    && point.SessionAutoClose > now && point.RecordedAt <= now.AddMinutes(1))
                    location = new(point.Latitude, point.Longitude, point.RecordedAt,
                        point.RecordedAt >= now.AddSeconds(-90));
            }
        }
        return new(order.Id, order.Status, order.DeliveryManId, order.DeliveryRouteId,
            driverName, order.ManagementVersion, location);
    }
}
