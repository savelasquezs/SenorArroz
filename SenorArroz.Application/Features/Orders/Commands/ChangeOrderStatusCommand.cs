using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class ChangeOrderStatusCommand : IRequest<OrderDto>
{
    public int Id { get; set; }
    public ChangeOrderStatusDto StatusChange { get; set; } = null!;
    public bool IsAutomaticDelivery { get; set; }
    public DateTime? AutoDeliveredAtUtc { get; set; }
    public long? AutoDeliveryTriggerLocationId { get; set; }
    public double? AutoDeliveryDepartureDistanceMeters { get; set; }
}
