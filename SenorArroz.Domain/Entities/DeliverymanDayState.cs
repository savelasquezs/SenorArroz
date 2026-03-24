using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Estado de liquidación del día por domiciliario (fecha operativa en Colombia).
/// </summary>
public class DeliverymanDayState : BaseEntity
{
    public int BranchId { get; set; }
    public int DeliverymanId { get; set; }

    /// <summary>Fecha calendario del día (solo fecha, zona Colombia).</summary>
    public DateOnly Date { get; set; }

    public DeliverymanDayLiquidationMode LiquidationMode { get; set; } = DeliverymanDayLiquidationMode.None;

    /// <summary>Si true, la tarjeta queda bloqueada hasta desbloqueo manual.</summary>
    public bool Blocked { get; set; }

    public DateTime? UnlockedAt { get; set; }
    public int? UnlockedById { get; set; }

    /// <summary>Fin de la última liquidación exitosa del día (UTC). Abonos y entregas posteriores abren un nuevo ciclo.</summary>
    public DateTime? LastLiquidationAtUtc { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual User Deliveryman { get; set; } = null!;
    public virtual User? UnlockedBy { get; set; }
}
