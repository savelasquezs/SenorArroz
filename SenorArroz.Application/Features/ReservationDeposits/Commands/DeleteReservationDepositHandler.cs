using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.ReservationDeposits.Commands;

public class DeleteReservationDepositHandler : IRequestHandler<DeleteReservationDepositCommand, Unit>
{
    private readonly IApplicationDbContext _context;

    public DeleteReservationDepositHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Unit> Handle(DeleteReservationDepositCommand request, CancellationToken cancellationToken)
    {
        var deposit = await _context.ReservationDeposits
            .Include(d => d.Order)
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

        if (deposit == null)
            throw new BusinessException("El abono no existe");

        var order = deposit.Order;
        if (order == null)
            throw new BusinessException("El pedido asociado no existe");

        if (order.Type != OrderType.Reservation)
            throw new BusinessException("Solo aplican abonos de reserva");

        if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Cancelled)
            throw new BusinessException("No se puede eliminar abonos de un pedido ya finalizado");

        _context.ReservationDeposits.Remove(deposit);
        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
