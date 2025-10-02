// SenorArroz.Application/Features/AppPayments/Commands/SettleMultipleAppPaymentsCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class SettleMultipleAppPaymentsCommand : IRequest<bool>
{
    public List<int> PaymentIds { get; set; } = new();
}
