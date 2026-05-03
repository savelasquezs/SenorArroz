using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class FreeDeliverymanFcmTokenResolver : IFreeDeliverymanFcmTokenResolver
{
    private sealed class DeliverymanIdRow
    {
        public int DeliveryManId { get; set; }
    }

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public FreeDeliverymanFcmTokenResolver(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
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

        var todayColombia = ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(_clock.UtcNow);
        var colombiaMidnightUnspecified = new DateTime(todayColombia.Year, todayColombia.Month, todayColombia.Day);
        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(colombiaMidnightUnspecified, colombiaMidnightUnspecified);

        var blockedDeliverymanIds = await _db.DeliverymanDayStates
            .AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Date == todayColombia && s.Blocked)
            .Select(s => s.DeliverymanId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Preferencia: delivery_man_assigned (persistido al asignar). Fallback legacy: ontheway / on_the_way.
        var assignmentTsExpr =
            $"""
             COALESCE(
               NULLIF(TRIM(COALESCE(o.status_times->>'{Order.DeliveryManAssignedStatusTimeKey}','')), ''),
               NULLIF(TRIM(COALESCE(o.status_times->>'ontheway','')), ''),
               NULLIF(TRIM(COALESCE(o.status_times->>'on_the_way','')), ''))
             """.ReplaceLineEndings(" ").Trim();

        // Placeholders {0}…{2} los parametriza SqlQueryRaw (Npgsql).
        var sql =
            """
            SELECT DISTINCT o.delivery_man_id AS "DeliveryManId"
            FROM "order" o
            WHERE o.branch_id = {0}
              AND o.delivery_man_id IS NOT NULL
              AND (
            """ + assignmentTsExpr + """
            ) IS NOT NULL
              AND (
            """ + assignmentTsExpr + """
            ) ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt ][0-9]{2}:[0-9]{2}'
              AND (
            """ + assignmentTsExpr + """
            )::timestamptz >= {1}
              AND (
            """ + assignmentTsExpr + """
            )::timestamptz <= {2}
            """;

        var assignedTodayRows = await _db.Database
            .SqlQueryRaw<DeliverymanIdRow>(sql, branchId, fromUtc, toUtc)
            .ToListAsync(cancellationToken);

        var assignedTodayIds = assignedTodayRows.Select(r => r.DeliveryManId).Distinct().ToList();

        var tokens = await _db.UserDeviceTokens
            .Where(t =>
                t.User.BranchId == branchId &&
                t.User.Role == UserRole.Deliveryman &&
                t.User.Active &&
                assignedTodayIds.Contains(t.UserId) &&
                !busyDeliverymanIds.Contains(t.UserId) &&
                !blockedDeliverymanIds.Contains(t.UserId))
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        return new FreeDeliverymanFcmTokensResult(tokens, busyDeliverymanIds.Count);
    }
}
