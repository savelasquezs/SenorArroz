namespace SenorArroz.Application.Features.ExpenseHeaders.DTOs;

public class UpdateExpenseHeaderDto
{
    public int? SupplierId { get; set; }
    public List<UpdateExpenseDetailDto>? ExpenseDetails { get; set; }
    public List<CreateExpenseBankPaymentDto>? ExpenseBankPayments { get; set; }
}

public class UpdateExpenseDetailDto
{
    public int? Id { get; set; } // null si es nuevo
    public int ExpenseId { get; set; }
    public int Quantity { get; set; }
    public int Amount { get; set; }
}

