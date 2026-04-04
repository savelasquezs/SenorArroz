using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetDeliverymanHistoryNeighborhoodsQuery : IRequest<List<DeliverymanHistoryNeighborhoodDto>>
{
    public int DeliveryManId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public OrderStatus? Status { get; set; }
    /// <summary>Si se indica, solo pedidos de esa sucursal (pestaña del historial).</summary>
    public int? BranchId { get; set; }
}
