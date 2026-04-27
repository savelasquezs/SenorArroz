using MediatR;

namespace SenorArroz.Application.Features.ReservationDeposits.Commands;

public class DeleteReservationDepositCommand : IRequest<Unit>
{
    public int Id { get; set; }
}
