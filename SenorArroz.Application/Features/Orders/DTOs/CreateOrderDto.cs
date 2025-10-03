using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.DTOs;

public class CreateOrderDto
{
    public int BranchId { get; set; }
    public int TakenById { get; set; }
    public int? CustomerId { get; set; }
    public int? AddressId { get; set; }
    public int? LoyaltyRuleId { get; set; }
    public OrderType Type { get; set; }
    public int? DeliveryFee { get; set; }
    public DateTime? ReservedFor { get; set; }
    public int Subtotal { get; set; }
    public int Total { get; set; }
    public int DiscountTotal { get; set; }
    public string? Notes { get; set; }
    public List<CreateOrderDetailDto> OrderDetails { get; set; } = new();
    public List<CreateOrderBankPaymentDto> BankPayments { get; set; } = new();
    public List<CreateOrderAppPaymentDto> AppPayments { get; set; } = new();
}

public class CreateOrderDetailDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public int UnitPrice { get; set; }
    public int Discount { get; set; }
    public string? Notes { get; set; }
}
