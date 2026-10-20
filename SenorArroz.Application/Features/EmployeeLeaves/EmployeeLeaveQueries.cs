using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.EmployeeLeaves;

public sealed class EmployeeLeaveQueries(IApplicationDbContext db, ICurrentUser current, IClock clock)
    : IRequestHandler<GetEmployeeLeavesQuery, PagedResult<EmployeeLeaveListDto>>,
      IRequestHandler<GetEmployeeLeaveQuery, EmployeeLeaveDetailDto>,
      IRequestHandler<GetEmployeeLeaveSummaryQuery, EmployeeLeaveSummaryDto>,
      IRequestHandler<GetEmployeeLeaveHistoryQuery, PagedResult<EmployeeLeaveHistoryDto>>
{
    public static bool IsReviewerSession(ICurrentUser current) =>
        string.Equals(current.Role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(current.Role, "Superadmin", StringComparison.OrdinalIgnoreCase);

    public static async Task<User> ActorAsync(IApplicationDbContext db, ICurrentUser current, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == current.Id
            && x.TenantId == current.TenantId && x.Active, ct);
        if (!current.IsAuthenticated || actor is null || actor.IsTerminal)
            throw new UnauthorizedAccessException("La operación requiere una cuenta personal activa.");
        return actor;
    }

    public static IQueryable<User> Reviewers(IApplicationDbContext db, EmployeeLeaveRequest request)
    {
        var requiresSuperadmin = request.ApplicantRole is nameof(UserRole.Admin) or nameof(UserRole.Superadmin);
        return db.Users.AsNoTracking().Where(x => x.TenantId == request.TenantId && x.Active
            && x.AccountType == EmployeeAccountType.Personal && x.Id != request.UserId
            && (x.Role == UserRole.Superadmin || !requiresSuperadmin && x.Role == UserRole.Admin
                && x.BranchId == request.BranchId));
    }

    public static bool CanReview(ICurrentUser current, User actor, EmployeeLeaveRequest request) =>
        IsReviewerSession(current) && EmployeeLeaveRules.CanReview(actor, request);

    public static void EnsureRead(ICurrentUser current, User actor, EmployeeLeaveRequest request)
    {
        if (request.TenantId != actor.TenantId
            || request.UserId != actor.Id && !CanReview(current, actor, request))
            throw new NotFoundException("Permiso no encontrado.");
    }

    private IQueryable<EmployeeLeaveRequest> ReviewScope(User actor)
    {
        if (!IsReviewerSession(current)) throw new UnauthorizedAccessException();
        return db.EmployeeLeaveRequests.AsNoTracking().Where(x => x.UserId != actor.Id
            && (actor.Role == UserRole.Superadmin || actor.Role == UserRole.Admin
                && x.BranchId == actor.BranchId && x.ApplicantRole != nameof(UserRole.Admin)
                && x.ApplicantRole != nameof(UserRole.Superadmin)));
    }

    public async Task<PagedResult<EmployeeLeaveListDto>> Handle(GetEmployeeLeavesQuery request, CancellationToken ct)
    {
        var actor = await ActorAsync(db, current, ct);
        var query = request.Review ? ReviewScope(actor)
            : db.EmployeeLeaveRequests.AsNoTracking().Where(x => x.UserId == actor.Id);
        if (request.BranchId.HasValue) query = query.Where(x => x.BranchId == request.BranchId);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (request.Status is not ("pending" or "approved" or "denied" or "postponed" or "cancelled"))
                throw new BusinessException("Estado no válido.");
            query = query.Where(x => x.Status == request.Status);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            if (search.Length > 120) throw new BusinessException("La búsqueda no puede superar 120 caracteres.");
            query = query.Where(x => x.ApplicantName.ToLower().Contains(search));
        }
        if (request.From is { Kind: not DateTimeKind.Utc } || request.To is { Kind: not DateTimeKind.Utc }
            || request.From > request.To) throw new BusinessException("Rango de fechas no válido.");
        if (request.From.HasValue) query = query.Where(x => x.EndsAt >= request.From.Value);
        if (request.To.HasValue) query = query.Where(x => x.StartsAt <= request.To.Value);
        var page = Math.Clamp(request.Page, 1, 100000);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var rows = await query.Include(x => x.Branch).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<EmployeeLeaveListDto>
        {
            Items = rows.Select(x => Map(x, actor.Id)).ToList(), TotalCount = total, Page = page,
            PageSize = size, TotalPages = (int)Math.Ceiling((double)total / size)
        };
    }

    public async Task<EmployeeLeaveDetailDto> Handle(GetEmployeeLeaveQuery request, CancellationToken ct)
    {
        var actor = await ActorAsync(db, current, ct);
        var leave = await db.EmployeeLeaveRequests.AsNoTracking().Include(x => x.Branch).Include(x => x.Attachments)
            .SingleOrDefaultAsync(x => x.PublicId == request.PublicId, ct)
            ?? throw new NotFoundException("Permiso no encontrado.");
        EnsureRead(current, actor, leave);
        var review = CanReview(current, actor, leave);
        var own = leave.UserId == actor.Id;
        var name = leave.DecidedByUserId.HasValue
            ? await db.Users.Where(x => x.Id == leave.DecidedByUserId).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return new EmployeeLeaveDetailDto(Map(leave, actor.Id), leave.DocumentNumber, leave.Justification,
            leave.IsPaid, leave.DecisionReason, leave.DecidedAt, name, leave.SupportRequired, leave.SupportDueAt,
            leave.SupportStatus, leave.ReturnedAt, leave.ReturnVerifiedAt, leave.VerificationReason,
            EmployeeLeaveRules.CanEdit(actor, leave), review,
            (own || review) && (leave.Status is "pending" or "postponed"
                || leave.Status == "approved" && leave.StartsAt > clock.UtcNow && leave.ReturnedAt == null),
            own && leave.Status == "approved" && leave.ReturnedAt == null && leave.StartsAt <= clock.UtcNow,
            await Reviewers(db, leave).AnyAsync(ct),
            leave.Attachments.OrderBy(x => x.CreatedAt).Select(x => new EmployeeLeaveAttachmentDto(
                x.Id, x.FileName, x.ContentType, x.Size, x.CreatedAt)).ToList());
    }

    public async Task<EmployeeLeaveSummaryDto> Handle(GetEmployeeLeaveSummaryQuery request, CancellationToken ct)
    {
        var actor = await ActorAsync(db, current, ct);
        var unread = await db.EmployeeLeaveRequests.CountAsync(x => x.UserId == actor.Id && x.Version > x.LastReadVersion, ct);
        var pending = IsReviewerSession(current) ? await ReviewScope(actor).CountAsync(x => x.Status == "pending", ct) : 0;
        return new(unread, pending);
    }

    public async Task<PagedResult<EmployeeLeaveHistoryDto>> Handle(GetEmployeeLeaveHistoryQuery request, CancellationToken ct)
    {
        var actor = await ActorAsync(db, current, ct);
        var leave = await db.EmployeeLeaveRequests.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == request.PublicId, ct)
            ?? throw new NotFoundException("Permiso no encontrado.");
        EnsureRead(current, actor, leave);
        var query = db.EntityAuditLogs.AsNoTracking().Where(x => x.EntityType == "employee_leave_request" && x.EntityId == leave.Id);
        var page = Math.Clamp(request.Page, 1, 100000);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.ChangedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * size).Take(size).Select(x => new EmployeeLeaveHistoryDto(x.Id,
                x.OperationType, x.ChangedAt, x.ChangedByNameSnapshot ?? db.Users.Where(u => u.Id == x.ChangedByUserId)
                    .Select(u => u.Name).FirstOrDefault() ?? "Usuario no disponible", x.SummaryText ?? "")).ToListAsync(ct);
        return new() { Items = items, Page = page, PageSize = size, TotalCount = total,
            TotalPages = (int)Math.Ceiling((double)total / size) };
    }

    public static EmployeeLeaveListDto Map(EmployeeLeaveRequest x, int actorId) => new(x.PublicId, x.UserId,
        x.BranchId, x.Branch?.Name ?? "", x.ApplicantName, x.ApplicantRole, x.Kind, x.Status,
        x.StartsAt, x.EndsAt, x.CreatedAt, x.UpdatedAt, x.Version, x.UserId == actorId && x.Version > x.LastReadVersion);
}
