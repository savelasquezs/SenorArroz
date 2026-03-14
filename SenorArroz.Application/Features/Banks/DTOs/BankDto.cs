// SenorArroz.Application/Features/Banks/DTOs/BankDto.cs
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Banks.DTOs;

public class BankDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool Active { get; set; }
    public BankType Type { get; set; }
    public bool IsHidden => Type == BankType.CashVault || Type == BankType.RealVault;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Statistics
    public int TotalApps { get; set; }
    public int ActiveApps { get; set; }
    public decimal CurrentBalance { get; set; }
}
