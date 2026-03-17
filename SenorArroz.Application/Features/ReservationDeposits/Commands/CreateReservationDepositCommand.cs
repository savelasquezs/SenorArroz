using MediatR;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;

namespace SenorArroz.Application.Features.ReservationDeposits.Commands;

public class CreateReservationDepositCommand : IRequest<ReservationDepositDto>
{
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public bool IsEffective { get; set; }
    public int? BankId { get; set; }
    public int? AppId { get; set; }
    public string? Notes { get; set; }
}
