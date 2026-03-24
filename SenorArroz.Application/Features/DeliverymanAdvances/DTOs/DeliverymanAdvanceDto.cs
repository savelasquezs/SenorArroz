using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.DeliverymanAdvances.DTOs;

public class DeliverymanAdvanceDto
{
    public int Id { get; set; }
    public int DeliverymanId { get; set; }
    public string DeliverymanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DeliverymanAdvancePaymentMethod PaymentMethod { get; set; }
    public int? BankId { get; set; }
    public string? BankName { get; set; }
    public int? ExpenseHeaderId { get; set; }
    public string? Notes { get; set; }
    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

