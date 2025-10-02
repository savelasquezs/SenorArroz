// SenorArroz.Application/Features/AppPayments/Commands/UnsettleAppPaymentCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class UnsettleAppPaymentCommand : IRequest<bool>
{
    public int Id { get; set; }
}
