// SenorArroz.Application/Features/BankPayments/Commands/DeleteBankPaymentCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public class DeleteBankPaymentCommand : IRequest<bool>
{
    public int Id { get; set; }
}
