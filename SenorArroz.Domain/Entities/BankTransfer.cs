using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Representa un movimiento de dinero entre dos bancos
/// </summary>
public class BankTransfer : BaseEntity
{
    public int FromBankId { get; set; }
    public int ToBankId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public int CreatedById { get; set; }

    // Navigation Properties
    public virtual Bank FromBank { get; set; } = null!;
    public virtual Bank ToBank { get; set; } = null!;
    public virtual User CreatedBy { get; set; } = null!;
}
