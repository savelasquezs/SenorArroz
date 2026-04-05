using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Registro de abono o descarga de efectivo contra el banco tipo <see cref="BankType.CashVault"/>.
/// No usa <see cref="BankTransfer"/> porque el efectivo físico no es una cuenta bancaria.
/// </summary>
public class CashVaultMovement : BaseEntity
{
    public int BranchId { get; set; }
    public int BankId { get; set; }
    public CashVaultMovementKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public int CreatedById { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual Bank Bank { get; set; } = null!;
    public virtual User CreatedBy { get; set; } = null!;
}
