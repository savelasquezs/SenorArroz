using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.DeliverymanAdvances.DTOs;

public class CreateDeliverymanAdvanceDto
{
    public int DeliverymanId { get; set; }
    public decimal Amount { get; set; }
    public DeliverymanAdvancePaymentMethod PaymentMethod { get; set; } = DeliverymanAdvancePaymentMethod.Cash;
    public int? BankId { get; set; }
    public int? ExpenseHeaderId { get; set; }
    public string? Notes { get; set; }
}

