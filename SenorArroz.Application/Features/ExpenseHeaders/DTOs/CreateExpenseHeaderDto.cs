namespace SenorArroz.Application.Features.ExpenseHeaders.DTOs;

public class CreateExpenseHeaderDto
{
    public int SupplierId { get; set; }
    public List<CreateExpenseDetailDto> ExpenseDetails { get; set; } = new();
    public List<CreateExpenseBankPaymentDto>? ExpenseBankPayments { get; set; }
}

public class CreateExpenseDetailDto
{
    public int ExpenseId { get; set; }
    public decimal Quantity { get; set; }
    public int Amount { get; set; }
    public decimal? Total { get; set; }
}

public class CreateExpenseBankPaymentDto
{
    public int BankId { get; set; }
    public decimal Amount { get; set; }
}


