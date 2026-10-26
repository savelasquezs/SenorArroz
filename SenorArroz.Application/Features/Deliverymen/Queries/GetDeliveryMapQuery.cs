using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public sealed record GetDeliveryMapQuery(int? BranchId, DateTime? Date, int Page = 1, int PageSize = 200)
    : IRequest<DeliveryMapPageDto>;

public sealed record DeliveryMapOrderDto(int Id, int? DeliveryManId, int? DeliveryRouteId, string Type,
    string Status, string StatusDisplayName, string? CustomerName, string? GuestName,
    string? AddressDescription, string? AddressAdditionalInfo, decimal? Latitude, decimal? Longitude);

public sealed record DeliveryMapPageDto(PagedResult<DeliveryMapOrderDto> Orders, List<DeliverymanLastLocationDto> Locations);

public sealed class GetDeliveryMapHandler(IApplicationDbContext db, ICurrentUser user, IBranchContext branches)
    : IRequestHandler<GetDeliveryMapQuery, DeliveryMapPageDto>
{
    public async Task<DeliveryMapPageDto> Handle(GetDeliveryMapQuery request, CancellationToken cancellationToken)
    {
        if (user.TenantId <= 0 || !OrderAccessRules.IsManagementRole(user.Role)) throw new BranchAccessDeniedException();
        var branchId = branches.RequireBranch(request.BranchId);
        if (!Roles.IsSuperadmin(user.Role) && branchId != user.BranchId) throw new BranchAccessDeniedException();
        if (request.Page < 1 || request.PageSize is < 1 or > 200)
            throw new BusinessException("La página del mapa no es válida.");
        var date = (request.Date ?? ColombiaTimeHelper.GetNowInColombia()).Date;
        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(date, date);
        var orders = db.Orders.AsNoTracking().Where(o => o.TenantId == user.TenantId && o.BranchId == branchId
            && o.Type == OrderType.Delivery && o.DeliveryManId != null
            && (o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Delivered)
            && ((o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc) || (o.ReservedFor >= fromUtc && o.ReservedFor <= toUtc)));
        var count = await orders.CountAsync(cancellationToken);
        var items = await orders.OrderByDescending(o => o.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(o => new DeliveryMapOrderDto(o.Id, o.DeliveryManId, o.DeliveryRouteId, "delivery",
                o.Status == OrderStatus.OnTheWay ? "on_the_way" : "delivered",
                o.Status == OrderStatus.OnTheWay ? "En camino" : "Entregado",
                o.Customer == null ? null : o.Customer.Name, o.GuestName,
                o.Address == null ? null : o.Address.AddressText, o.Address == null ? null : o.Address.AdditionalInfo,
                o.Address == null ? null : o.Address.Latitude, o.Address == null ? null : o.Address.Longitude))
            .ToListAsync(cancellationToken);
        var locations = request.Page == 1
            ? await db.Users.AsNoTracking().Where(u => u.TenantId == user.TenantId && u.BranchId == branchId
                && u.AccountType != EmployeeAccountType.Terminal
                && (u.Role == UserRole.Deliveryman || (u.DeliveryAppEnabled && (u.Role == UserRole.Admin || u.Role == UserRole.Cashier))))
                .SelectMany(u => db.DeliverymanLocations.Where(l => l.TenantId == user.TenantId && l.DeliverymanId == u.Id && l.RecordedAt >= fromUtc && l.RecordedAt <= toUtc)
                    .OrderByDescending(l => l.RecordedAt).ThenByDescending(l => l.Id).Take(1))
                .Select(l => new DeliverymanLastLocationDto { DeliverymanId = l.DeliverymanId, DeliveryRouteId = l.DeliveryRouteId,
                    Latitude = (double)l.Latitude, Longitude = (double)l.Longitude, RecordedAt = l.RecordedAt })
                .ToListAsync(cancellationToken)
            : [];
        return new(new PagedResult<DeliveryMapOrderDto> { Items = items, TotalCount = count, Page = request.Page, PageSize = request.PageSize, TotalPages = (int)Math.Ceiling(count / (double)request.PageSize) }, locations);
    }
}
