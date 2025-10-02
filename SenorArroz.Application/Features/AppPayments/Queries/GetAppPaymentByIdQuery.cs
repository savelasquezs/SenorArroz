// SenorArroz.Application/Features/AppPayments/Queries/GetAppPaymentByIdQuery.cs
using MediatR;
using SenorArroz.Application.Features.AppPayments.DTOs;

namespace SenorArroz.Application.Features.AppPayments.Queries;

public class GetAppPaymentByIdQuery : IRequest<AppPaymentDto?>
{
    public int Id { get; set; }
}
