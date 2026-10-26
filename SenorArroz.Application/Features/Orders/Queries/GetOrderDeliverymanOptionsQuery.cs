using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Orders.Queries;

public sealed record GetOrderDeliverymanOptionsQuery(DateTime? FromDate, DateTime? ToDate, int? BranchId, bool RemoteOnly)
    : IRequest<List<OrderDeliverymanOptionDto>>;

public sealed class GetOrderDeliverymanOptionsHandler(IApplicationDbContext db, ICurrentUser user, IBranchContext branches)
    : IRequestHandler<GetOrderDeliverymanOptionsQuery, List<OrderDeliverymanOptionDto>>
{
    public async Task<List<OrderDeliverymanOptionDto>> Handle(GetOrderDeliverymanOptionsQuery request, CancellationToken cancellationToken)
    {
        if (user.TenantId <= 0 || !OrderAccessRules.IsManagementRole(user.Role)) throw new BranchAccessDeniedException();
        var branchId = Roles.IsSuperadmin(user.Role) ? branches.ResolveOptional(request.BranchId) : user.BranchId;
        int? origin = null;
        if (request.RemoteOnly)
        {
            origin = branches.RequireBranch();
            if (request.BranchId is not > 0 || request.BranchId == origin
                || !await db.Branches.AsNoTracking().AnyAsync(b => b.Id == request.BranchId && b.TenantId == user.TenantId, cancellationToken))
                throw new BranchAccessDeniedException();
            branchId = request.BranchId;
        }
        var query = db.Orders.AsNoTracking().Where(o => o.TenantId == user.TenantId && o.Type == OrderType.Delivery && o.DeliveryManId != null);
        if (branchId.HasValue) query = query.Where(o => o.BranchId == branchId.Value);
        if (origin.HasValue) query = query.Where(o => o.OriginBranchId == origin.Value && o.BranchId != origin.Value);
        if (request.FromDate.HasValue || request.ToDate.HasValue)
        {
            var from = (request.FromDate ?? request.ToDate)!.Value.Date;
            var to = (request.ToDate ?? request.FromDate)!.Value.Date;
            var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(from, to);
            query = query.Where(o => (o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc)
                || (o.ReservedFor.HasValue && o.ReservedFor >= fromUtc && o.ReservedFor <= toUtc));
        }
        var rows = await query.Select(o => new { Id = o.DeliveryManId!.Value, Name = o.DeliveryMan == null ? "" : o.DeliveryMan.Name })
            .Distinct().ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.Id).Select(group => new OrderDeliverymanOptionDto(group.Key,
                string.IsNullOrWhiteSpace(group.First().Name) ? $"Domiciliario #{group.Key}" : group.First().Name.Trim()))
            .OrderBy(option => option.Name).ToList();
    }
}
