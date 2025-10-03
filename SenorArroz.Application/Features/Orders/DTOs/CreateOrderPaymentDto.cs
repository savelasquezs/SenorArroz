namespace SenorArroz.Application.Features.Orders.DTOs;

public class CreateOrderBankPaymentDto
{
    public int BankId { get; set; }
    public decimal Amount { get; set; }
}

public class CreateOrderAppPaymentDto
{
    public int AppId { get; set; }
    public decimal Amount { get; set; }
}
