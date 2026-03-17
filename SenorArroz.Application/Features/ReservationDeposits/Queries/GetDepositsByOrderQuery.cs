using MediatR;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;

namespace SenorArroz.Application.Features.ReservationDeposits.Queries;

public class GetDepositsByOrderQuery : IRequest<List<ReservationDepositDto>>
{
    public int OrderId { get; set; }
}
