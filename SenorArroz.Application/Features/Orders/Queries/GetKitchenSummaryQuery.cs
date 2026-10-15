using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Queries;

public sealed class GetKitchenSummaryQuery : IRequest<List<KitchenSummaryOrderDto>>
{
    public DateTime ColombiaDate { get; set; }
    public int? BranchId { get; set; }
}
