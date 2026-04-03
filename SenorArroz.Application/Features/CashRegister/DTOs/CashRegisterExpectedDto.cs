namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class CashRegisterExpectedDto
{
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal CashFromOrders { get; set; }
    public decimal CashDeposits { get; set; }
    public decimal CashExpenses { get; set; }
    public DateTime AsOf { get; set; }
    public DateTime? LastClosureAt { get; set; }
    public List<BankExpectedBalanceDto> Banks { get; set; } = new();
}

public class BankExpectedBalanceDto
{
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal ExpectedBalance { get; set; }
}
