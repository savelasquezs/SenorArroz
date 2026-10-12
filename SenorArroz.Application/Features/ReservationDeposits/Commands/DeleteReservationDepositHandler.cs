using SenorArroz.Application.Common.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.ReservationDeposits.Commands;

public class DeleteReservationDepositHandler : IRequestHandler<DeleteReservationDepositCommand, Unit>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser? _currentUser;
    private readonly IBranchContext? _branchContext;

    public DeleteReservationDepositHandler(IApplicationDbContext context, ICurrentUser? currentUser = null, IBranchContext? branchContext = null)
    {
        _context = context;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<Unit> Handle(DeleteReservationDepositCommand request, CancellationToken cancellationToken)
    {
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

            if (order.Type != OrderType.Reservation)
                throw new BusinessException("Solo aplican abonos de reserva");

            if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Cancelled)
                throw new BusinessException("No se puede eliminar abonos de un pedido ya finalizado");

            var linkedBankPayment = await _context.BankPayments
                .FirstOrDefaultAsync(bp => bp.SourceReservationDepositId == deposit.Id, cancellationToken);
            if (linkedBankPayment != null)
                _context.BankPayments.Remove(linkedBankPayment);

            _context.ReservationDeposits.Remove(deposit);
            await _context.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            return Unit.Value;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
