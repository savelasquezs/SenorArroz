using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class CreateCashVaultMovementDto
{
    public CashVaultMovementKind Kind { get; set; }
    /// <summary>Obligatorio para abono. Para descarga, obligatorio salvo que <see cref="WithdrawAll"/> sea true.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Solo para <see cref="CashVaultMovementKind.WithdrawFromVault"/>: descarga el saldo esperado completo del banco Caja Mayor Efectivo.</summary>
    public bool WithdrawAll { get; set; }

    public string? Note { get; set; }
}

public class CashVaultMovementDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public CashVaultMovementKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
}
