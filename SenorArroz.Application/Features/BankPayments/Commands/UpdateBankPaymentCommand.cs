using MediatR;
using SenorArroz.Application.Features.BankPayments.DTOs;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class UpdateBankPaymentCommand : IRequest<BankPaymentDto>
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
}

