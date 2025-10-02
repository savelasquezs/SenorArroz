// SenorArroz.Application/Features/AppPayments/Queries/GetAppPaymentsQuery.cs
using MediatR;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.AppPayments.Queries;

public class GetAppPaymentsQuery : IRequest<PagedResult<AppPaymentDto>>
{
    public int? OrderId { get; set; }
    public int? AppId { get; set; }
    public int? BankId { get; set; }
    public int? BranchId { get; set; }
    public bool? Settled { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "createdAt";
    public string SortOrder { get; set; } = "desc";
}
