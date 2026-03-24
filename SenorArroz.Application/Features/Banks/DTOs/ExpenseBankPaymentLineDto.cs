namespace SenorArroz.Application.Features.Banks.DTOs;

public class ExpenseBankPaymentLineDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ExpenseHeaderId { get; set; }
    public int BranchId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
}
