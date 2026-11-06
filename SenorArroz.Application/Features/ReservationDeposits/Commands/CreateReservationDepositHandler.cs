using SenorArroz.Application.Common.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ReservationDeposits.Commands;

public class CreateReservationDepositHandler : IRequestHandler<CreateReservationDepositCommand, ReservationDepositDto>
{
    private readonly IReservationDepositRepository _depositRepository;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;
    private readonly IClock _clock;

    public CreateReservationDepositHandler(
        IReservationDepositRepository depositRepository,
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IBranchContext branchContext,
        IClock clock)
    {
        _depositRepository = depositRepository;
        _context = context;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _clock = clock;
    }

    public async Task<ReservationDepositDto> Handle(CreateReservationDepositCommand request, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
            throw new BusinessException("El monto debe ser mayor a 0");

        if (!request.IsEffective && request.BankId == null && request.AppId == null)
            throw new BusinessException("Debe especificar banco o app para un abono no efectivo");

        await using var tx = _context.Database.IsRelational() && _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order == null)
            throw new BusinessException("El pedido no existe");
        OrderAccessRules.EnsureAccess(order, _currentUser, _branchContext);

        if (request.BankId.HasValue)
        {
            var bankBranchId = await _context.Banks
                .Where(x => x.Id == request.BankId.Value && x.Active && x.Type == BankType.Normal)
                .Select(x => (int?)x.BranchId)
                .FirstOrDefaultAsync(cancellationToken);
            if (bankBranchId != order.BranchId)
                throw new BranchScopeMismatchException();
        }

        if (request.AppId.HasValue)
        {
            var appBranchId = await _context.Apps
                .Where(x => x.Id == request.AppId.Value && x.Active && x.Bank.Active)
                .Select(x => (int?)x.Bank.BranchId)
                .FirstOrDefaultAsync(cancellationToken);
            if (appBranchId != order.BranchId)
                throw new BranchScopeMismatchException();
        }

        if (order.Type != OrderType.Reservation)
            throw new BusinessException("Solo se pueden registrar abonos para pedidos de tipo reserva");

        if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Cancelled)
            throw new BusinessException("No se puede abonar a un pedido ya finalizado");

        order = await ReservationReceiptHelper.LoadPaymentsAsync(_context, order.Id, cancellationToken);

        var deposit = new ReservationDeposit
        {
            OrderId = request.OrderId,
            BranchId = order.BranchId,
            TenantId = order.TenantId,
            Amount = request.Amount,
            IsEffective = request.IsEffective,
            BankId = request.BankId,
            AppId = request.AppId,
            ReceivedAt = _clock.UtcNow,
            ReceivedById = _currentUser.Id,
            Notes = request.Notes
        };

        await ReservationReceiptHelper.CreateAsync(_context, order, new[] { deposit }, cancellationToken);
        var created = await _depositRepository.GetByIdAsync(deposit.Id, cancellationToken)
            ?? throw new BusinessException("No se pudo leer el abono registrado");
        if (tx is not null) await tx.CommitAsync(cancellationToken);

        return new ReservationDepositDto
        {
            Id = created.Id,
            OrderId = created.OrderId,
            BranchId = created.BranchId,
            Amount = created.Amount,
            IsEffective = created.IsEffective,
            CashAppliedToOrder = created.CashAppliedToOrder,
            BankId = created.BankId,
            BankName = created.Bank?.Name,
            AppId = created.AppId,
            AppName = created.App?.Name,
            ReceivedAt = created.ReceivedAt,
            ReceivedById = created.ReceivedById,
            ReceivedByName = created.ReceivedBy?.Name ?? string.Empty,
            Notes = created.Notes,
            CreatedAt = created.CreatedAt
        };
    }
}
