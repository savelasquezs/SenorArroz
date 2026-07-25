using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Application.Features.CashRegister.Helpers;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetClosureAuditSummaryHandler : IRequestHandler<GetClosureAuditSummaryQuery, CashClosureAuditSummaryDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetClosureAuditSummaryHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<CashClosureAuditSummaryDto?> Handle(GetClosureAuditSummaryQuery request, CancellationToken cancellationToken)
    {
        var closure = await _context.CashRegisterClosures
            .AsNoTracking()
            .Include(x => x.Branch)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

        if (closure == null)
            return null;

        if (!Roles.IsSuperadmin(_currentUser.Role) && closure.BranchId != _currentUser.BranchId)
            throw new UnauthorizedAccessException("No tienes acceso a la auditoría de otra sucursal.");

        var businessDate = ColombiaTimeHelper.ConvertUtcToColombiaCalendarDate(closure.ClosedAt);
        var previousClosure = await _context.CashRegisterClosures
            .AsNoTracking()
            .Where(x => x.BranchId == closure.BranchId && x.ClosedAt < closure.ClosedAt)
            .OrderByDescending(x => x.ClosedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var periodStartUtc = previousClosure?.ClosedAt ?? ColombiaTimeHelper.ColombiaCalendarDayStartUtc(businessDate);
        var periodEndUtc = closure.ClosedAt;

        var dispatch = await _context.DailyAuditDispatches
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CashRegisterClosureId == closure.Id, cancellationToken);

        var logs = await _context.EntityAuditLogs
            .AsNoTracking()
            .Where(x => x.BranchId == closure.BranchId && x.ChangedAt > periodStartUtc && x.ChangedAt <= periodEndUtc)
            .OrderByDescending(x => x.ChangedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        var referencedProductIds = CashClosureAuditMapper.ReferencedProductIds(logs);
        var productNames = await _context.Products
            .AsNoTracking()
            .Where(x => referencedProductIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var cancelledOrderIds = logs
            .Where(x => x.EntityType == "order" && x.OperationType == "cancelled")
            .Select(x => x.EntityId)
            .Distinct()
            .ToList();
        var cancelledOrderProductRows = await _context.OrderDetails
            .AsNoTracking()
            .Where(x => cancelledOrderIds.Contains(x.OrderId))
            .Select(x => new { x.OrderId, x.Product.Name })
            .ToListAsync(cancellationToken);
        var cancelledOrderProductNames = cancelledOrderProductRows
            .GroupBy(x => x.OrderId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.Name).Distinct().ToList());

        var auditEvents = CashClosureAuditMapper
            .Consolidate(logs, productNames, cancelledOrderProductNames)
            .Where(CashClosureAuditMapper.ShouldIncludeInClosureAudit)
            .ToList();
        var events = auditEvents.Select(CashClosureAuditMapper.ToDto).ToList();
        var groups = auditEvents
            .GroupBy(CashClosureAuditMapper.GroupKey)
            .Select(g => new CashClosureAuditGroupDto
            {
                Key = g.Key,
                Title = CashClosureAuditMapper.GroupTitle(g.Key),
                EventCount = g.Count(),
                NetDifference = g.Sum(x => x.Difference),
                Details = g.OrderByDescending(x => x.ChangedAt)
                    .Select(x => x.DetailText)
                    .ToList()
            })
            .OrderBy(x => x.Title)
            .ToList();

        return new CashClosureAuditSummaryDto
        {
            CashClosureId = closure.Id,
            BranchId = closure.BranchId,
            BranchName = closure.Branch?.Name ?? string.Empty,
            BusinessDate = businessDate.ToString("yyyy-MM-dd"),
            DispatchStatus = dispatch?.DispatchStatus ?? "not_sent",
            DispatchError = dispatch?.DispatchError,
            DispatchedAt = dispatch?.DispatchedAt,
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = periodEndUtc,
            RecipientEmails = ParseEmails(dispatch?.RecipientEmailsJson),
            Groups = groups,
            Events = events
        };
    }

    private static List<string> ParseEmails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
