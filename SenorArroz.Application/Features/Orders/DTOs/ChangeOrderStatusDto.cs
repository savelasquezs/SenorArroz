using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.DTOs;

public class ChangeOrderStatusDto
{
    public OrderStatus Status { get; set; }
    public string? Reason { get; set; }
}

public class AssignDeliveryManDto
{
    public int DeliveryManId { get; set; }
}

public class CancelOrderDto
{
    public string Reason { get; set; } = string.Empty;
}
