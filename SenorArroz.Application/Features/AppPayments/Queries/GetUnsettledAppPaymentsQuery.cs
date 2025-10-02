// SenorArroz.Application/Features/AppPayments/Queries/GetUnsettledAppPaymentsQuery.cs
using MediatR;
using SenorArroz.Application.Features.AppPayments.DTOs;

namespace SenorArroz.Application.Features.AppPayments.Queries;

public class GetUnsettledAppPaymentsQuery : IRequest<IEnumerable<AppPaymentDto>>
{
    public int? AppId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
