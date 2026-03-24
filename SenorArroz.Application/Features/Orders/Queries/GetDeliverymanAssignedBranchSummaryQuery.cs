using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetDeliverymanAssignedBranchSummaryQuery : IRequest<List<DeliverymanAssignedBranchSummaryDto>>
{
    public int DeliveryManId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    /// <summary>Si se indica, solo cuenta pedidos en ese estado (p. ej. entregados para historial).</summary>
    public OrderStatus? Status { get; set; }
}
