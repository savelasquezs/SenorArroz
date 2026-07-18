using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.DTOs;

public class UpdateOrderDto
{
    public int? CustomerId { get; set; }
    public OrderType? Type { get; set; }
    public int? AddressId { get; set; }
    public string? GuestName { get; set; }
    public int? DeliveryFee { get; set; }
    public DateTime? ReservedFor { get; set; }
    public DateTime? PrepareAt { get; set; }
    public int? Subtotal { get; set; }
    public int? Total { get; set; }
    public int? DiscountTotal { get; set; }
    public bool? FreeDeliveryRequested { get; set; }
    public OrderBenefitType? AppliedBenefitType { get; set; }
    public int? AppliedBenefitSourceId { get; set; }
    public string? AppliedBenefitCode { get; set; }
    public string? AppliedBenefitLabel { get; set; }
    public LoyaltyRewardType? AppliedBenefitRewardType { get; set; }
    public decimal? AppliedBenefitAmount { get; set; }
    public string? AppliedBenefitSnapshot { get; set; }
    public string? ManualBenefitReason { get; set; }
    public int? ManualBenefitGiftProductId { get; set; }
    public string? Notes { get; set; }
    public List<UpdateOrderDetailDto>? OrderDetails { get; set; }
    public bool DeleteReservationAssociatedPayments { get; set; }
}

public class UpdateOrderDetailDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public int UnitPrice { get; set; }
    public int Discount { get; set; }
    public string? Notes { get; set; }
}
