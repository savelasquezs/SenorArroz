// SenorArroz.Application/Features/BankPayments/Commands/UnverifyBankPaymentCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class UnverifyBankPaymentCommand : IRequest<bool>
{
    public int Id { get; set; }
}
