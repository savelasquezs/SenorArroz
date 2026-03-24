using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetDeliverymanAssignedBranchSummaryQuery : IRequest<List<DeliverymanAssignedBranchSummaryDto>>
{
    public int DeliveryManId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
