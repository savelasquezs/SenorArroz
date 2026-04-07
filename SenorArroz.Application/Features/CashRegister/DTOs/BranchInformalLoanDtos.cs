using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class BranchInformalLoanDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime? DeactivatedAt { get; set; }
    public int? DeactivatedById { get; set; }
    public string? DeactivatedByName { get; set; }
    public string? DeactivationNotes { get; set; }
}

public class CreateBranchInformalLoanDto
{
    [MaxLength(500)]
    public string? Concept { get; set; }

    public decimal? Amount { get; set; }

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
