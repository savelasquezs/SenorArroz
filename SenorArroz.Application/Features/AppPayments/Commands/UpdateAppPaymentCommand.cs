using MediatR;
using SenorArroz.Application.Features.AppPayments.DTOs;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class UpdateAppPaymentCommand : IRequest<AppPaymentDto>
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
}

