// SenorArroz.Application/Features/BankPayments/DTOs/BankPaymentDto.cs
namespace SenorArroz.Application.Features.BankPayments.DTOs;

public class BankPaymentDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
