// SenorArroz.Application/Features/AppPayments/Commands/CreateAppPaymentCommand.cs
using MediatR;
using SenorArroz.Application.Features.AppPayments.DTOs;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class CreateAppPaymentCommand : IRequest<AppPaymentDto>
{
    public int OrderId { get; set; }
    public int AppId { get; set; }
    public decimal Amount { get; set; }
}
