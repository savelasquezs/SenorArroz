using SenorArroz.Domain.Enums;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Application.Features.AppPayments.DTOs;

namespace SenorArroz.Application.Features.Orders.DTOs;

public class OrderDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public int TakenById { get; set; }
    public string TakenByName { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public int? AddressId { get; set; }
    public string? AddressDescription { get; set; }
    public int? LoyaltyRuleId { get; set; }
    public string? LoyaltyRuleName { get; set; }
    public int? DeliveryManId { get; set; }
    public string? DeliveryManName { get; set; }
    public string? GuestName { get; set; }
    public OrderType? Type { get; set; }
    public string? TypeDisplayName { get; set; }
    public int? DeliveryFee { get; set; }
    public DateTime? ReservedFor { get; set; }
    public OrderStatus Status { get; set; }
    public string? StatusDisplayName { get; set; }
    public Dictionary<string, DateTime> StatusTimes { get; set; } = new();
    public int Subtotal { get; set; }
    public int Total { get; set; }
    public int DiscountTotal { get; set; }
    public string? Notes { get; set; }
    public string? CancelledReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<BankPaymentDto> BankPayments { get; set; } = new();
    public List<AppPaymentDto> AppPayments { get; set; } = new();
}
