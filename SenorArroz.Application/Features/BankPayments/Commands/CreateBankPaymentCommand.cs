// SenorArroz.Application/Features/BankPayments/Commands/CreateBankPaymentCommand.cs
using MediatR;
using SenorArroz.Application.Features.BankPayments.DTOs;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class CreateBankPaymentCommand : IRequest<BankPaymentDto>
{
    public int OrderId { get; set; }
    public int BankId { get; set; }
    public decimal Amount { get; set; }
}
