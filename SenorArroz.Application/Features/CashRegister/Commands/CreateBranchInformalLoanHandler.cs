using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CreateBranchInformalLoanHandler : IRequestHandler<CreateBranchInformalLoanCommand, BranchInformalLoanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public CreateBranchInformalLoanHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<BranchInformalLoanDto> Handle(CreateBranchInformalLoanCommand request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        int branchId = request.BranchId ?? _currentUser.BranchId;

        if (dto.DeliveryAdvance is { Lines.Count: > 0 } adv)
            return await CreateDeliveryAdvanceAsync(branchId, adv, cancellationToken);

        if (string.IsNullOrWhiteSpace(dto.Concept))
            throw new InvalidOperationException("El concepto es obligatorio.");
        if (dto.Amount is null)
            throw new InvalidOperationException("El monto es obligatorio.");

        var entityManual = new BranchInformalLoan
        {
            BranchId = branchId,
            Concept = dto.Concept.Trim(),
            Amount = dto.Amount.Value,
            CreatedById = _currentUser.Id
        };

        _context.BranchInformalLoans.Add(entityManual);
        await _context.SaveChangesAsync(cancellationToken);

        return await MapLoanDto(entityManual.Id, cancellationToken);
    }

    private async Task<BranchInformalLoanDto> CreateDeliveryAdvanceAsync(
        int branchId,
        CreateDeliveryAdvanceInformalLoanDto adv,
        CancellationToken cancellationToken)
    {
        var lines = adv.Lines;
        var orderIds = lines.Select(l => l.OrderId).Distinct().ToList();
        if (orderIds.Count != lines.Count)
            throw new InvalidOperationException("No repitas el mismo pedido en las líneas.");

        var today = ColombiaTimeHelper.GetTodayDateOnlyColombia();
        var dmOk = await _context.DeliverymanDayStates
            .AsNoTracking()
            .AnyAsync(s =>
                s.BranchId == branchId
                && s.DeliverymanId == adv.DeliverymanId
                && s.Date == today
                && s.Blocked
                && s.LiquidationMode == DeliverymanDayLiquidationMode.FullLiquidation,
                cancellationToken);
        if (!dmOk)
            throw new InvalidOperationException(
                "El domiciliario no está liquidado con modo total y bloqueado hoy, o no pertenece a esta sucursal.");

        var dmName = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == adv.DeliverymanId && u.BranchId == branchId && u.Role == UserRole.Deliveryman)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (dmName is null)
            throw new InvalidOperationException("Domiciliario no válido para esta sucursal.");

        var conflictIds = await _context.BranchInformalLoanExemptOrders
            .Where(e => orderIds.Contains(e.OrderId) && e.Loan.BranchId == branchId && e.Loan.DeactivatedAt == null)
            .Select(e => e.OrderId)
            .ToListAsync(cancellationToken);
        if (conflictIds.Count > 0)
            throw new InvalidOperationException(
                $"Estos pedidos ya están cubiertos por otro préstamo activo: {string.Join(", ", conflictIds)}.");

        var orders = await _context.Orders
            .Where(o => orderIds.Contains(o.Id) && o.BranchId == branchId)
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        if (orders.Count != orderIds.Count)
            throw new InvalidOperationException("Uno o más pedidos no existen o no son de esta sucursal.");

        foreach (var line in lines)
        {
            var o = orders[line.OrderId];
            if (o.Type != OrderType.Delivery)
                throw new InvalidOperationException($"El pedido #{o.Id} no es domicilio.");
            if (o.Status != OrderStatus.Ready && o.Status != OrderStatus.OnTheWay)
                throw new InvalidOperationException($"El pedido #{o.Id} debe estar listo o en camino.");
            if (!DeliveryAdvanceVueltoHelper.IsValidVueltoAdd(o.Total, line.VueltoAdd))
                throw new InvalidOperationException($"Vuelto inválido para el pedido #{o.Id}.");
        }

        decimal total = 0;
        var parts = new List<string>(lines.Count);
        foreach (var line in lines.OrderBy(l => l.OrderId))
        {
            var o = orders[line.OrderId];
            var carry = (decimal)o.Total + line.VueltoAdd;
            total += carry;
            parts.Add($"#{o.Id} {carry:0}");
        }

        var concept = $"{string.Join(", ", parts)}, total {total:0}, {dmName}".Trim();
        if (concept.Length > 500)
            concept = concept[..497] + "...";

        var entity = new BranchInformalLoan
        {
            BranchId = branchId,
            Concept = concept,
            Amount = total,
            CreatedById = _currentUser.Id,
            ExemptOrders = orderIds.Select(oid => new BranchInformalLoanExemptOrder { OrderId = oid }).ToList()
        };

        _context.BranchInformalLoans.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return await MapLoanDto(entity.Id, cancellationToken);
    }

    private async Task<BranchInformalLoanDto> MapLoanDto(int loanId, CancellationToken cancellationToken)
    {
        return await _context.BranchInformalLoans
            .AsNoTracking()
            .Where(l => l.Id == loanId)
            .Select(l => new BranchInformalLoanDto
            {
                Id = l.Id,
                BranchId = l.BranchId,
                Concept = l.Concept,
                Amount = l.Amount,
                CreatedAt = l.CreatedAt,
                CreatedById = l.CreatedById,
                CreatedByName = l.CreatedBy.Name,
                DeactivatedAt = l.DeactivatedAt,
                DeactivatedById = l.DeactivatedById,
                DeactivatedByName = l.DeactivatedBy != null ? l.DeactivatedBy.Name : null,
                DeactivationNotes = l.DeactivationNotes
            })
            .FirstAsync(cancellationToken);
    }
}
