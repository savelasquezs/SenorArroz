using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

/// <summary>
/// Pedidos entregados del domiciliario en el período, filtrados por instante de entrega y ciclo de liquidación (mismo criterio que day-summary).
/// </summary>
public class GetDeliverymanOrdersQuery : IRequest<PagedResult<OrderDto>>
{
    public int DeliverymanId { get; set; }
    public DateTime? Date { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
