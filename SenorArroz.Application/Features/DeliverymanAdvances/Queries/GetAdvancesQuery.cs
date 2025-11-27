using MediatR;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Queries;

public class GetAdvancesQuery : IRequest<PagedResult<DeliverymanAdvanceDto>>
{
    public int? DeliverymanId { get; set; }
    public int? BranchId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "createdAt";
    public string SortOrder { get; set; } = "desc";
}

