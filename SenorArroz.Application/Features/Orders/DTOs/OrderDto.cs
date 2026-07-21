using SenorArroz.Domain.Enums;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;

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
    /// <summary>Apartamento, torre, referencia, etc. (Address.AdditionalInfo)</summary>
    public string? AddressAdditionalInfo { get; set; }
    public int? NeighborhoodId { get; set; }
    public string? NeighborhoodName { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    /// <summary>Texto de premio fidelidad (snapshot o paso del ciclo); mantiene nombre JSON legacy para clientes.</summary>
    public string? LoyaltyRuleName { get; set; }
    public int? LoyaltyCycleStepId { get; set; }
    public string? LoyaltyRewardSnapshot { get; set; }
    public int? DeliveryManId { get; set; }
    /// <summary>Ruta de domicilio (métricas SLA) cuando aplica.</summary>
    public int? DeliveryRouteId { get; set; }
    /// <summary>Advertencias al planificar la ruta (saltos de línea), cuando hay ruta asociada.</summary>
    public string? DeliveryRoutePlanningWarnings { get; set; }
    public DateTime? DeliveryRouteStartedAtUtc { get; set; }
    public DateTime? DeliveryRouteCompletedAtUtc { get; set; }
    public int? DeliveryRouteMetaDurationSeconds { get; set; }
    public int? DeliveryRouteActualDurationSeconds { get; set; }
    public string? DeliveryManName { get; set; }
    public string? GuestName { get; set; }
    public int? DeliveryAppConnectionId { get; set; }
    public string? ExternalOrderId { get; set; }
    public string? OrderSource { get; set; }
    public string? ExternalFulfillmentProvider { get; set; }
    public OrderType? Type { get; set; }
    public string? TypeDisplayName { get; set; }
    public int? DeliveryFee { get; set; }
    public DateTime? ReservedFor { get; set; }
    public DateTime? PrepareAt { get; set; }
    public OrderStatus Status { get; set; }
    public string? StatusDisplayName { get; set; }
    public Dictionary<string, DateTime> StatusTimes { get; set; } = new();
    public int Subtotal { get; set; }
    public int Total { get; set; }
    public int DiscountTotal { get; set; }
    public bool FreeDeliveryRequested { get; set; }
    public OrderBenefitType AppliedBenefitType { get; set; }
    public int? AppliedBenefitSourceId { get; set; }
    public string? AppliedBenefitCode { get; set; }
    public string? AppliedBenefitLabel { get; set; }
    public LoyaltyRewardType? AppliedBenefitRewardType { get; set; }
    public decimal? AppliedBenefitAmount { get; set; }
    public string? AppliedBenefitSnapshot { get; set; }
    public string? ManualBenefitReason { get; set; }
    public int? ManualBenefitGrantedByUserId { get; set; }
    public string? ManualBenefitGrantedByUserName { get; set; }
    public DateTime? ManualBenefitGrantedAt { get; set; }
    public int? ManualBenefitGiftProductId { get; set; }
    public string? Notes { get; set; }
    public string? CancelledReason { get; set; }
    /// <summary>Efectivo ya cobrado en sucursal; nada que cobrar en entrega.</summary>
    public bool PaidInStoreCash { get; set; }
    public DateTime? PaidInStoreCashAt { get; set; }
    public int? PaidInStoreCashAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<BankPaymentDto> BankPayments { get; set; } = new();
    public List<AppPaymentDto> AppPayments { get; set; } = new();
    public decimal TotalDeposited { get; set; }
    /// <summary>Abonos de anticipo (solo pedidos tipo reserva; listado enriquecido).</summary>
    public List<ReservationDepositDto> ReservationDeposits { get; set; } = new();
    /// <summary>Líneas resumidas (p. ej. búsqueda/listado con detalles cargados).</summary>
    public List<OrderLineSummaryDto> SummaryLines { get; set; } = new();
}
