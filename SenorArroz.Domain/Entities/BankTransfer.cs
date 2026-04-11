using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Representa un movimiento de dinero entre dos bancos
/// </summary>
public class BankTransfer : BaseEntity
{
    /// <summary>Null = extremo efectivo de caja (sale hacia <see cref="ToBank"/>).</summary>
    public int? FromBankId { get; set; }
    /// <summary>Null = extremo efectivo de caja (entra desde <see cref="FromBank"/>).</summary>
    public int? ToBankId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public int CreatedById { get; set; }

    // Navigation Properties
    public virtual Bank? FromBank { get; set; }
    public virtual Bank? ToBank { get; set; }
    public virtual User CreatedBy { get; set; } = null!;
}
