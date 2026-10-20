using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.EmployeeLunches;

public sealed record ChangeEmployeeLunchCommand(bool Finish, string Password, int? UserId = null, int? LunchId = null)
    : IRequest<EmployeeLunchDto>;
public sealed record CorrectEmployeeLunchCommand(int Id, DateTime StartedAt, DateTime? EndedAt,
    DateTime ExpectedUpdatedAt, string Reason) : IRequest<EmployeeLunchDto>;

public sealed class EmployeeLunchCommands(IApplicationDbContext db, ICurrentUser actor, IBranchContext branch,
    IPasswordService passwords, IClock clock, IOrderNotificationService notifications,
    ILogger<EmployeeLunchCommands> logger)
    : IRequestHandler<ChangeEmployeeLunchCommand, EmployeeLunchDto>,
      IRequestHandler<CorrectEmployeeLunchCommand, EmployeeLunchDto>
{
    public async Task<EmployeeLunchDto> Handle(ChangeEmployeeLunchCommand request, CancellationToken ct)
    {
        var id = request.UserId ?? actor.Id;
        var terminal = actor.Role.Equals("Kitchen", StringComparison.OrdinalIgnoreCase)
            && await db.Users.AnyAsync(x => x.Id == actor.Id && x.Active && x.WebAccessEnabled
                && x.Role == UserRole.Kitchen && x.AccountType == EmployeeAccountType.Terminal, ct);
        if (id != actor.Id && !terminal) throw new UnauthorizedAccessException();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Active, ct)
            ?? throw new NotFoundException("Empleado no encontrado.");
        if (terminal && (user.Role != UserRole.Kitchen || user.IsTerminal || user.BranchId != actor.BranchId))
            throw new UnauthorizedAccessException();
        EmployeePasswordConfirmation.Verify(user, request.Password, passwords);

        var now = new DateTime(clock.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        var lunch = await db.EmployeeLunches.SingleOrDefaultAsync(x => x.UserId == id && x.EndedAt == null, ct);
        if (request.Finish)
        {
            if (lunch is null || lunch.Id != request.LunchId)
                throw new BusinessException("El almuerzo cambió. Actualiza el estado.");
            lunch.EndedAt = now;
            lunch.EndedByUserId = actor.Id;
            lunch.UpdatedAt = now;
        }
        else
        {
            if (lunch is not null) throw new BusinessException("El empleado ya está almorzando.");
            if (await db.Orders.AnyAsync(x => x.DeliveryManId == id && x.Status != OrderStatus.Delivered && x.Status != OrderStatus.Cancelled, ct))
                throw new BusinessException("Termina los pedidos pendientes antes de salir a almorzar.");
            lunch = new EmployeeLunch
            {
                UserId = id, BranchId = user.BranchId, StartedAt = now, StartedByUserId = actor.Id,
                CreatedAt = now, UpdatedAt = now
            };
            db.EmployeeLunches.Add(lunch);
        }
        await db.SaveChangesAsync(ct);
        await Notify(lunch);
        return EmployeeLunchQueries.Map(lunch);
    }

    public async Task<EmployeeLunchDto> Handle(CorrectEmployeeLunchCommand request, CancellationToken ct)
    {
        if (!Roles.IsAdminOrSuperadmin(actor.Role)) throw new UnauthorizedAccessException();
        var lunch = await db.EmployeeLunches.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException("Almuerzo no encontrado.");
        branch.EnsureAccess(lunch.BranchId);
        if (request.ExpectedUpdatedAt != lunch.UpdatedAt) throw new OrderConcurrencyException();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw new BusinessException("Indica un motivo de máximo 500 caracteres.");
        if (request.StartedAt.Kind != DateTimeKind.Utc || request.EndedAt is { Kind: not DateTimeKind.Utc }
            || request.StartedAt > clock.UtcNow || request.EndedAt > clock.UtcNow || request.EndedAt < request.StartedAt)
            throw new BusinessException("Las fechas deben ser UTC, no futuras y el regreso debe ser posterior a la salida.");
        if (await db.EmployeeLunches.AnyAsync(x => x.UserId == lunch.UserId && x.Id != lunch.Id
            && (request.EndedAt == null || x.StartedAt < request.EndedAt)
            && (x.EndedAt == null || x.EndedAt > request.StartedAt), ct))
            throw new BusinessException("El intervalo se cruza con otro almuerzo.");
        var before = JsonSerializer.Serialize(EmployeeLunchQueries.Map(lunch));
        var earliest = lunch.StartedAt < request.StartedAt ? lunch.StartedAt : request.StartedAt;
        var latest = lunch.EndedAt == null || request.EndedAt == null ? clock.UtcNow
            : lunch.EndedAt > request.EndedAt ? lunch.EndedAt.Value : request.EndedAt.Value;
        foreach (var session in await db.DeliveryWorkSessions.Where(x => x.DeliverymanId == lunch.UserId
            && x.StartedAt <= latest && x.EndedAt >= earliest).ToListAsync(ct))
            session.StayAnalysisLastLocationId = null;
        lunch.StartedAt = request.StartedAt;
        lunch.EndedAt = request.EndedAt;
        lunch.EndedByUserId = request.EndedAt.HasValue ? actor.Id : null;
        lunch.UpdatedAt = new DateTime(clock.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        db.EntityAuditLogs.Add(new EntityAuditLog
        {
            BranchId = lunch.BranchId, EntityType = "employee_lunch", EntityId = lunch.Id,
            OperationType = "corrected", ChangedAt = clock.UtcNow, BusinessDate = clock.UtcNow.AddHours(-5).Date,
            ChangedByUserId = actor.Id, SummaryText = request.Reason.Trim(),
            BeforeJson = before, AfterJson = JsonSerializer.Serialize(EmployeeLunchQueries.Map(lunch))
        });
        await db.SaveChangesAsync(ct);
        await Notify(lunch);
        return EmployeeLunchQueries.Map(lunch);
    }

    private async Task Notify(EmployeeLunch lunch)
    {
        try { await notifications.NotifyEmployeeLunchChanged(lunch.BranchId, lunch.UserId); }
        catch (Exception ex) { logger.LogWarning(ex, "Almuerzo guardado; no se pudo notificar al empleado {UserId}.", lunch.UserId); }
    }
}
