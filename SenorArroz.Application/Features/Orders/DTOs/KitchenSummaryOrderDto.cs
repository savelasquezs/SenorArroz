using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.DTOs;

/// <summary>Lightweight order and complete kitchen lines for the selected Colombia day.</summary>
public sealed class KitchenSummaryOrderDto
{
    public int Id { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime? PrepareAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderDetailDto> KitchenDetails { get; set; } = [];
}
