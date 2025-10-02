// SenorArroz.Application/Features/AppPayments/DTOs/AppPaymentDto.cs
namespace SenorArroz.Application.Features.AppPayments.DTOs;

public class AppPaymentDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsSetted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
