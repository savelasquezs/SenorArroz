using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class FreeDeliverymanFcmTokenResolver : IFreeDeliverymanFcmTokenResolver
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public FreeDeliverymanFcmTokenResolver(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<FreeDeliverymanFcmTokensResult> ResolveAsync(
        int branchId,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var branch = await _db.Branches
            .AsNoTracking()
            .Where(x => x.Id == branchId)
            .Select(x => new
            {
                x.Latitude,
                x.Longitude,
                x.DeliveryTrackingAllowedDistanceMeters,
                x.DeliveryTrackingLightIntervalSeconds,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (branch?.Latitude is null || branch.Longitude is null)
            return new FreeDeliverymanFcmTokensResult([], 0, 0);

        // "Libre" significa que no tiene ningún pedido activo asignado,
        // independientemente del estado concreto del flujo de domicilio.
        var busyDeliverymanIds = await _db.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                        && o.DeliveryManId != null
                        && o.Status != OrderStatus.Delivered
                        && o.Status != OrderStatus.Cancelled)
            .Select(o => o.DeliveryManId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var candidateTokens = await _db.UserDeviceTokens
            .AsNoTracking()
            .Where(t => t.User.BranchId == branchId
                        && t.User.Role == UserRole.Deliveryman
                        && t.User.Active
                        && !busyDeliverymanIds.Contains(t.UserId))
            .Select(t => new { t.UserId, t.Token })
            .ToListAsync(cancellationToken);

        var candidateIds = candidateTokens
            .Select(x => x.UserId)
            .Distinct()
            .ToList();
        if (candidateIds.Count == 0)
            return new FreeDeliverymanFcmTokensResult([], busyDeliverymanIds.Count, 0);

        var activeSessionIds = await _db.DeliveryWorkSessions
            .AsNoTracking()
            .Where(x => x.BranchId == branchId
                        && candidateIds.Contains(x.DeliverymanId)
                        && x.Status == DeliveryWorkSessionStatus.Active
                        && x.AutoCloseAt > nowUtc)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (activeSessionIds.Count == 0)
            return new FreeDeliverymanFcmTokensResult([], busyDeliverymanIds.Count, 0);

        // En modo libre la app reporta cada LightInterval. Se admite un minuto
        // adicional por red/procesamiento, pero nunca una ventana menor a 2 min.
        var freshnessSeconds = Math.Max(
            120,
            Math.Max(1, branch.DeliveryTrackingLightIntervalSeconds) + 60);
        var recordedAfterUtc = nowUtc.AddSeconds(-freshnessSeconds);

        var recentLocations = await _db.DeliverymanLocations
            .AsNoTracking()
            .Where(x => x.WorkSessionId.HasValue
                        && activeSessionIds.Contains(x.WorkSessionId.Value)
                        && x.RecordedAt >= recordedAfterUtc
                        && x.RecordedAt <= nowUtc.AddMinutes(1)
                        && x.GpsEnabled != false)
            .OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new
            {
                x.DeliverymanId,
                x.Latitude,
                x.Longitude,
            })
            .ToListAsync(cancellationToken);

        var allowedDistanceMeters = Math.Max(1, branch.DeliveryTrackingAllowedDistanceMeters);
        var deliverymenAtBranch = recentLocations
            .GroupBy(x => x.DeliverymanId)
            .Select(x => x.First())
            .Where(x => GeoHelper.HaversineDistanceMeters(
                (double)x.Latitude,
                (double)x.Longitude,
                (double)branch.Latitude.Value,
                (double)branch.Longitude.Value) <= allowedDistanceMeters)
            .Select(x => x.DeliverymanId)
            .ToHashSet();

        var tokens = candidateTokens
            .Where(x => deliverymenAtBranch.Contains(x.UserId))
            .Select(x => x.Token)
            .Distinct()
            .ToList();

        return new FreeDeliverymanFcmTokensResult(
            tokens,
            busyDeliverymanIds.Count,
            deliverymenAtBranch.Count);
    }
}
