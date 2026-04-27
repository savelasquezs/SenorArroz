using MediatR;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;

namespace SenorArroz.Application.Features.ReservationDeposits.Commands;

public class UpdateReservationDepositCommand : IRequest<ReservationDepositDto>
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
}
