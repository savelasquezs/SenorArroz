using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Deliverymen.DTOs;

public class SettleDeliverymanBankLineDto
{
    public int BankId { get; set; }
    public decimal Amount { get; set; }
}

public class SettleDeliverymanExpenseLineDto
{
    public int ExpenseHeaderId { get; set; }
    public decimal Amount { get; set; }
}

public class SettleDeliverymanDayDto
{
    /// <summary>Fecha operativa YYYY-MM-DD (Colombia).</summary>
    public string Date { get; set; } = string.Empty;

    public decimal BaseAmount { get; set; }

    public decimal CashAmount { get; set; }

    public List<SettleDeliverymanBankLineDto> BankTransfers { get; set; } = new();

    public List<SettleDeliverymanExpenseLineDto> ExpenseOffsets { get; set; } = new();

    public DeliverymanDayLiquidationMode Mode { get; set; }
}
