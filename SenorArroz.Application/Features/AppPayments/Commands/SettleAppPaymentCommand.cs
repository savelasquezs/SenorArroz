// SenorArroz.Application/Features/AppPayments/Commands/SettleAppPaymentCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class SettleAppPaymentCommand : IRequest<bool>
{
    public int Id { get; set; }
}
