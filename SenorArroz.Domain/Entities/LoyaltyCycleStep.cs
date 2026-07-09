using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Paso del programa de fidelidad por sucursal (ciclo 1..N; el premio del pedido entregado k-ésimo con cliente).
/// </summary>
public class LoyaltyCycleStep : BaseEntity
{
    public int BranchId { get; set; }
    /// <summary>Posición en el ciclo (1-based).</summary>
    public int StepIndex { get; set; }
    /// <summary>Texto para ticket / mensaje al cliente.</summary>
    public string RewardLabel { get; set; } = string.Empty;
    /// <summary>Nombre opcional para administración (p. ej. Excel).</summary>
    public string? StepName { get; set; }
    public LoyaltyRewardType? RewardType { get; set; }
    public int? GiftProductId { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual Branch Branch { get; set; } = null!;
    public virtual Product? GiftProduct { get; set; }
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
