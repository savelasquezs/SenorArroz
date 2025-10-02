// SenorArroz.Application/Features/Apps/DTOs/AppDto.cs
namespace SenorArroz.Application.Features.Apps.DTOs;

public class AppDto
{
    public int Id { get; set; }
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Statistics
    public decimal TotalPayments { get; set; }
    public decimal UnsettledPayments { get; set; }
    public int TotalPaymentsCount { get; set; }
    public int UnsettledPaymentsCount { get; set; }
}
