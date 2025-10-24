// SenorArroz.Application/Features/BankPayments/Commands/VerifyBankPaymentCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class VerifyBankPaymentCommand : IRequest<bool>
{
    public int Id { get; set; }
}
