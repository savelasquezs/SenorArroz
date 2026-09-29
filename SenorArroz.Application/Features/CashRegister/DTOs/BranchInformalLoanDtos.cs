using System.ComponentModel.DataAnnotations;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class BranchInformalLoanDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public int? BankId { get; set; }
    public string? BankName { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime? DeactivatedAt { get; set; }
    public int? DeactivatedById { get; set; }
    public string? DeactivatedByName { get; set; }
    public string? DeactivationNotes { get; set; }
    public decimal TotalPaid { get; set; }
    public int PaymentsCount { get; set; }
}

public class BranchInformalLoanPaymentDto
{
    public int Id { get; set; }
    public BranchInformalLoanPaymentKind Kind { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public int? ExpenseHeaderId { get; set; }
}

public class BranchInformalLoanHistoryDto : BranchInformalLoanDto
{
    public decimal InitialAmount { get; set; }
    public List<BranchInformalLoanPaymentDto> Payments { get; set; } = new();
}

public class CreateBranchInformalLoanPaymentDto
{
    [Range(typeof(decimal), "0.01", "9999999999")]
    public decimal Amount { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class CreateBranchInformalLoanDto
{
    [MaxLength(500)]
    public string? Concept { get; set; }

    public decimal? Amount { get; set; }

    public int? BankId { get; set; }

    /// <summary>
    /// Si viene informado, ignora Concept/Amount del root y crea préstamo con pedidos exentos del cuadre.
    /// </summary>
    public CreateDeliveryAdvanceInformalLoanDto? DeliveryAdvance { get; set; }
}

public class CreateDeliveryAdvanceInformalLoanDto
{
    [Required]
    public int DeliverymanId { get; set; }

    [Required]
    [MinLength(1)]
    public List<DeliveryAdvanceLineDto> Lines { get; set; } = new();
}

public class DeliveryAdvanceLineDto
{
    public int OrderId { get; set; }

    /// <summary>COP adicionales para redondear (0 si el total ya es múltiplo de 100.000).</summary>
    public decimal VueltoAdd { get; set; }
}

public class DeliveryAdvanceOrderRowDto
{
    public int Id { get; set; }
    public int Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public string AddressSummary { get; set; } = string.Empty;
}

public class LiquidatedDeliverymanOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class DeactivateBranchInformalLoanDto
{
    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class UpdateBranchInformalLoanDto
{
    [Required]
    [MaxLength(500)]
    public string Concept { get; set; } = string.Empty;

    [Required]
    public decimal Amount { get; set; }
}
