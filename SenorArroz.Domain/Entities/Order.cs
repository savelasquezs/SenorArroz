using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;
using System.Text.Json;

namespace SenorArroz.Domain.Entities;

public class Order : BaseEntity
{
    /// <summary>Clave en <see cref="StatusTimes"/> para el instante UTC en que se asignó o reasignó el domiciliario.</summary>
    public const string DeliveryManAssignedStatusTimeKey = "delivery_man_assigned";

    public int BranchId { get; set; }
    public int TakenById { get; set; }
    public int? CustomerId { get; set; }
    public int? AddressId { get; set; }
    /// <summary>Paso del ciclo de fidelidad aplicado al marcar entregado (con cliente).</summary>
    public int? LoyaltyCycleStepId { get; set; }
    /// <summary>Copia del texto de premio al momento de entrega.</summary>
    public string? LoyaltyRewardSnapshot { get; set; }
    /// <summary>Ruta de domicilio activa o última asociada (null si no aplica).</summary>
    public int? DeliveryRouteId { get; set; }
    public int? DeliveryManId { get; set; }
    public string? GuestName { get; set; }

    public OrderType? Type { get; set; }
    public int? DeliveryFee { get; set; }
    public DateTime? ReservedFor { get; set; }
    /// <summary>
    /// Hora en que el pedido debe aparecer en cocina (para reservas).
    /// Por defecto reserved_for - 1h, modificable.
    /// </summary>
    public DateTime? PrepareAt { get; set; }
    /// <summary>
    /// Momento en que se emitió ReservationReady a cocina. Evita notificaciones duplicadas.
    /// </summary>
    public DateTime? PreparedNotifiedAt { get; set; }
    public OrderStatus Status { get; set; }

    // JSONB field para timestamps - mapea directamente a "status_times"
    public string StatusTimes { get; set; } = "{}";

    public int Subtotal { get; set; } = 0;
    public int Total { get; set; } = 0;
    public int DiscountTotal { get; set; } = 0;
    /// <summary>Indica que el descuento tipo domicilio gratis fue aplicado sobre las líneas del pedido.</summary>
    public bool FreeDeliveryRequested { get; set; }
    public OrderBenefitType AppliedBenefitType { get; set; } = OrderBenefitType.None;
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

    /// <summary>Efectivo pendiente ya cobrado en sucursal; el domiciliario no cobra en entrega.</summary>
    public bool PaidInStoreCash { get; set; }
    /// <summary>Momento en que se marcó el cobro en tienda (UTC).</summary>
    public DateTime? PaidInStoreCashAt { get; set; }
    /// <summary>Snapshot COP del efectivo reconocido al activar (para cuadre de caja).</summary>
    public int? PaidInStoreCashAmount { get; set; }

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual User TakenBy { get; set; } = null!;
    public virtual Customer? Customer { get; set; }
    public virtual Address? Address { get; set; }
    public virtual LoyaltyCycleStep? LoyaltyCycleStep { get; set; }
    public virtual User? DeliveryMan { get; set; }
    public virtual DeliveryRoute? DeliveryRoute { get; set; }

    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    public virtual ICollection<BankPayment> BankPayments { get; set; } = new List<BankPayment>();
    public virtual ICollection<AppPayment> AppPayments { get; set; } = new List<AppPayment>();
    public virtual ICollection<ReservationDeposit> Deposits { get; set; } = new List<ReservationDeposit>();

    // Helper methods para StatusTimes
    public Dictionary<string, DateTime> GetStatusTimes()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(StatusTimes) ?? new Dictionary<string, DateTime>();
        }
        catch
        {
            return new Dictionary<string, DateTime>();
        }
    }

    public void SetStatusTimes(Dictionary<string, DateTime> statusTimes)
    {
        StatusTimes = JsonSerializer.Serialize(statusTimes);
    }

    public void AddStatusTime(OrderStatus status, DateTime timestamp)
    {
        var statusTimes = GetStatusTimes();
        statusTimes[status.ToString().ToLowerInvariant()] = timestamp;
        SetStatusTimes(statusTimes);
    }

    /// <summary>Registra el momento UTC de asignación/reasignación de domiciliario en <see cref="StatusTimes"/>.</summary>
    public void TouchDeliveryManAssignedAtUtc(DateTime utcTimestamp)
    {
        var utc = utcTimestamp.Kind == DateTimeKind.Utc
            ? utcTimestamp
            : DateTime.SpecifyKind(utcTimestamp.ToUniversalTime(), DateTimeKind.Utc);
        var statusTimes = GetStatusTimes();
        statusTimes[DeliveryManAssignedStatusTimeKey] = utc;
        SetStatusTimes(statusTimes);
    }
}
