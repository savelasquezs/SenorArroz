using SenorArroz.Application.Common.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ReservationDeposits.Commands;

public class UpdateReservationDepositHandler : IRequestHandler<UpdateReservationDepositCommand, ReservationDepositDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser? _currentUser;
    private readonly IReservationDepositRepository _depositRepository;
    private readonly IBranchContext _branchContext;

    public UpdateReservationDepositHandler(
        IApplicationDbContext context,
        IReservationDepositRepository depositRepository,
        IBranchContext branchContext, ICurrentUser? currentUser = null)
    {
        _context = context;
        _depositRepository = depositRepository;
        _branchContext = branchContext;
        _currentUser = currentUser;
    }

    public async Task<ReservationDepositDto> Handle(UpdateReservationDepositCommand request, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
            throw new BusinessException("El monto debe ser mayor a 0");

        await using var tx = _context.Database.IsRelational() && _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;

        try
        {
            var deposit = await _context.ReservationDeposits
                .Include(d => d.Order)
                .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

            if (deposit == null)
                throw new BusinessException("El abono no existe");


            var order = deposit.Order;
            if (order == null)
                throw new BusinessException("El pedido asociado no existe");

            if (_currentUser is not null)
                OrderAccessRules.EnsureAccess(order, _currentUser, _branchContext);
            else
                _branchContext?.EnsureAccess(order.BranchId);

            // The order changes from Reservation to Delivery/Onsite when it becomes
            // Ready, but its original reservation receipt must remain editable.
            if (order.Type != OrderType.Reservation && !order.ReservedFor.HasValue)
                throw new BusinessException("Solo aplican abonos de reserva");

            if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Cancelled)
                throw new BusinessException("No se puede modificar abonos de un pedido ya finalizado");

            await ReservationReceiptHelper.UpdateAsync(_context, deposit, request.Amount, cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);

            var refreshed = await _depositRepository.GetByIdAsync(deposit.Id, cancellationToken);
            if (refreshed == null)
                throw new BusinessException("No se pudo leer el abono actualizado");

            return new ReservationDepositDto
            {
                Id = refreshed.Id,
                OrderId = refreshed.OrderId,
                BranchId = refreshed.BranchId,
                Amount = refreshed.Amount,
                IsEffective = refreshed.IsEffective,
                CashAppliedToOrder = refreshed.CashAppliedToOrder,
                BankId = refreshed.BankId,
                BankName = refreshed.Bank?.Name,
                AppId = refreshed.AppId,
                AppName = refreshed.App?.Name,
                ReceivedAt = refreshed.ReceivedAt,
                ReceivedById = refreshed.ReceivedById,
                ReceivedByName = refreshed.ReceivedBy?.Name ?? string.Empty,
                Notes = refreshed.Notes,
                CreatedAt = refreshed.CreatedAt
            };
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
