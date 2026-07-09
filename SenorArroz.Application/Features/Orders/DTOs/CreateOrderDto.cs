using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.DTOs;

public class CreateOrderDto
{
    public int BranchId { get; set; }
    public int TakenById { get; set; }
    public int? CustomerId { get; set; }
    public int? AddressId { get; set; }
    public string? GuestName { get; set; }
    public OrderType Type { get; set; }
    public int? DeliveryFee { get; set; }
    public DateTime? ReservedFor { get; set; }
    public DateTime? PrepareAt { get; set; }
    public int? Subtotal { get; set; }
    public int? Total { get; set; }
    public int? DiscountTotal { get; set; }
    public bool FreeDeliveryRequested { get; set; }
    public OrderBenefitType? AppliedBenefitType { get; set; }
    public int? AppliedBenefitSourceId { get; set; }
    public string? AppliedBenefitCode { get; set; }
    public string? AppliedBenefitLabel { get; set; }
    public LoyaltyRewardType? AppliedBenefitRewardType { get; set; }
    public decimal? AppliedBenefitAmount { get; set; }
    public string? AppliedBenefitSnapshot { get; set; }
    public string? Notes { get; set; }
    public List<CreateOrderDetailDto> OrderDetails { get; set; } = new();
    public List<CreateOrderBankPaymentDto> BankPayments { get; set; } = new();
    public List<CreateOrderAppPaymentDto> AppPayments { get; set; } = new();

    /// <summary>Efectivo ya cobrado en sucursal al crear el pedido (caja).</summary>
    public bool PaidInStoreCash { get; set; }

    /// <summary>Opcional. Si <see cref="PaidInStoreCash"/> es true, monto COP (validado contra el remanente).</summary>
    public int? PaidInStoreCashAmount { get; set; }
}

public class CreateOrderDetailDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public int UnitPrice { get; set; }
    public int Discount { get; set; }
    public string? Notes { get; set; }
}
