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
    [Required]
    [MaxLength(200)]
    public string Concept { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}

public class DeactivateBranchInformalLoanDto
{
    [MaxLength(500)]
    public string? Notes { get; set; }
}
