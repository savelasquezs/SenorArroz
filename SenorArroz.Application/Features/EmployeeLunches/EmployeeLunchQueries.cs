using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.EmployeeLunches;

public sealed record EmployeeLunchDto(int Id, DateTime StartedAt, DateTime? EndedAt, DateTime UpdatedAt);
public sealed record EmployeeLunchStateDto(int UserId, string Name, string Role,
    EmployeeLunchDto? ActiveLunch, List<EmployeeLunchDto> Lunches, DateTime ServerTimeUtc);
public sealed record GetEmployeeLunchQuery : IRequest<EmployeeLunchStateDto>;
public sealed record GetBranchLunchesQuery(DateOnly? Date = null, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<EmployeeLunchStateDto>>;

public sealed class EmployeeLunchQueries(IApplicationDbContext db, ICurrentUser actor,
    IBranchContext branch, IClock clock)
    : IRequestHandler<GetEmployeeLunchQuery, EmployeeLunchStateDto>,
      IRequestHandler<GetBranchLunchesQuery, PagedResult<EmployeeLunchStateDto>>
{
    public async Task<EmployeeLunchStateDto> Handle(GetEmployeeLunchQuery request, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.Id && x.Active, ct)
            ?? throw new NotFoundException("Usuario no encontrado.");
        if (user.IsTerminal) throw new UnauthorizedAccessException();
        return (await States([user], ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(clock.UtcNow), ct))[0];
    }

    public async Task<PagedResult<EmployeeLunchStateDto>> Handle(GetBranchLunchesQuery request, CancellationToken ct)
    {
        var kitchen = actor.Role.Equals("Kitchen", StringComparison.OrdinalIgnoreCase);
        if (!Roles.IsSuperadminOrAdminOrCashier(actor.Role) && !kitchen) throw new UnauthorizedAccessException();
        if (kitchen && !await db.Users.AnyAsync(x => x.Id == actor.Id && x.Active && x.WebAccessEnabled
            && x.Role == UserRole.Kitchen && x.AccountType == EmployeeAccountType.Terminal, ct))
            throw new UnauthorizedAccessException();
        var branchId = branch.RequireBranch();
        var date = request.Date ?? ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(clock.UtcNow);
        var from = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(5);
        var to = from.AddDays(1);
        var users = db.Users.AsNoTracking().Where(x => x.AccountType == EmployeeAccountType.Personal
            && (x.Active && x.BranchId == branchId || db.EmployeeLunches.Any(l => l.UserId == x.Id && l.BranchId == branchId
                && l.StartedAt < to && (l.EndedAt == null || l.EndedAt > from))));
        if (kitchen)
        {
            if (date != ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(clock.UtcNow)) throw new UnauthorizedAccessException();
            users = users.Where(x => x.Active && x.BranchId == branchId && x.Role == UserRole.Kitchen);
        }
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var total = await users.CountAsync(ct);
        return new PagedResult<EmployeeLunchStateDto>
        {
            Items = await States(await users.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct), date, ct, branchId),
            TotalCount = total, Page = page, PageSize = size, TotalPages = (int)Math.Ceiling((double)total / size)
        };
    }

    private async Task<List<EmployeeLunchStateDto>> States(List<User> users, DateOnly date, CancellationToken ct, int? branchId = null)
    {
        var ids = users.Select(x => x.Id).ToArray();
        var from = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(5);
        var to = from.AddDays(1);
        var lunches = await db.EmployeeLunches.AsNoTracking()
            .Where(x => ids.Contains(x.UserId) && (!branchId.HasValue || x.BranchId == branchId)
                && (x.EndedAt == null || x.StartedAt < to && x.EndedAt > from))
            .OrderByDescending(x => x.StartedAt).ToListAsync(ct);
        return users.Select(u => new EmployeeLunchStateDto(u.Id, u.Name, u.Role.ToString() ?? "",
            lunches.Where(x => x.UserId == u.Id && x.EndedAt == null).Select(Map).SingleOrDefault(),
            lunches.Where(x => x.UserId == u.Id && x.StartedAt < to && (x.EndedAt == null || x.EndedAt > from)).Select(Map).ToList(),
            clock.UtcNow)).ToList();
    }

    public static EmployeeLunchDto Map(EmployeeLunch lunch) => new(lunch.Id, lunch.StartedAt, lunch.EndedAt, lunch.UpdatedAt);
}
