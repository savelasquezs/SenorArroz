// SenorArroz.Application/Features/BankPayments/Queries/GetBankPaymentsQuery.cs
using MediatR;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.BankPayments.Queries;

public class GetBankPaymentsQuery : IRequest<PagedResult<BankPaymentDto>>
{
    public int? OrderId { get; set; }
    public int? BankId { get; set; }
    public int? BranchId { get; set; }
    public bool? Verified { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "createdAt";
    public string SortOrder { get; set; } = "desc";
}
