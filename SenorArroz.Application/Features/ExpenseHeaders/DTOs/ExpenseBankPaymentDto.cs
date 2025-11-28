namespace SenorArroz.Application.Features.ExpenseHeaders.DTOs;

public class ExpenseBankPaymentDto
{
    public int Id { get; set; }
    public int ExpenseHeaderId { get; set; }
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}

