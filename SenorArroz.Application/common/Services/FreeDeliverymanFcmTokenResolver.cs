using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class FreeDeliverymanFcmTokenResolver : IFreeDeliverymanFcmTokenResolver
{
    private readonly IApplicationDbContext _db;

    public FreeDeliverymanFcmTokenResolver(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<FreeDeliverymanFcmTokensResult> ResolveAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var busyDeliverymanIds = await _db.Orders
            .Where(o => o.BranchId == branchId &&
                        o.Status == OrderStatus.OnTheWay &&
                        o.DeliveryManId != null)
            .Select(o => o.DeliveryManId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var tokens = await _db.UserDeviceTokens
            .Where(t =>
                t.User.BranchId == branchId &&
                t.User.Role == UserRole.Deliveryman &&
                t.User.Active &&
                !busyDeliverymanIds.Contains(t.UserId))
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        return new FreeDeliverymanFcmTokensResult(tokens, busyDeliverymanIds.Count);
    }
}
