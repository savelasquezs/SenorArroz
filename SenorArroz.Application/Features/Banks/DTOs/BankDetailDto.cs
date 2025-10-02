// SenorArroz.Application/Features/Banks/DTOs/BankDetailDto.cs
namespace SenorArroz.Application.Features.Banks.DTOs;

public class BankDetailDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Statistics
    public int TotalApps { get; set; }
    public int ActiveApps { get; set; }
    public decimal TotalBankPayments { get; set; }
    public decimal TotalExpenseBankPayments { get; set; }
    public decimal CurrentBalance { get; set; }
}
