using MediatR;
using SenorArroz.Application.Features.BankTransfers.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.BankTransfers.Queries;

public class GetBankTransfersQuery : IRequest<PagedResult<BankTransferDto>>
{
    public int? BranchId { get; set; }
    public int? FromBankId { get; set; }
    public int? ToBankId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "createdAt";
    public string SortOrder { get; set; } = "desc";
}
