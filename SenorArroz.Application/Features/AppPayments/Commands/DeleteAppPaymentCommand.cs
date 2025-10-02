// SenorArroz.Application/Features/AppPayments/Commands/DeleteAppPaymentCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class DeleteAppPaymentCommand : IRequest<bool>
{
    public int Id { get; set; }
}
