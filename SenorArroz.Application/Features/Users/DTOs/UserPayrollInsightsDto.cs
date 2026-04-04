namespace SenorArroz.Application.Features.Users.DTOs;

public class UserPayrollInsightsDto
{
    public UserPayrollLinkedExpenseDto? LinkedExpense { get; set; }
    public decimal DeliveryFeePayRate { get; set; }
    public string FromDate { get; set; } = string.Empty;
    public string ToDate { get; set; } = string.Empty;
    public string SeriesGranularity { get; set; } = string.Empty;
    public UserPayrollPeriodTotalsDto Period { get; set; } = new();
    public List<UserPayrollSeriesPointDto> Series { get; set; } = new();
}

public class UserPayrollLinkedExpenseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UserPayrollPeriodTotalsDto
{
    public decimal ExpenseLinesTotal { get; set; }
    public List<UserPayrollExpenseLineItemDto> ExpenseLines { get; set; } = new();
    public int DeliveredOrdersCount { get; set; }
    public decimal SumDeliveryFee { get; set; }
    public decimal PayableDeliveryFee { get; set; }
    public bool IsDeliveryman { get; set; }
}

public class UserPayrollExpenseLineItemDto
{
    public int DetailId { get; set; }
    public int HeaderId { get; set; }
    public DateTime HeaderCreatedAt { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
}

public class UserPayrollSeriesPointDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal ExpenseLinesTotal { get; set; }
    public int DeliveredOrdersCount { get; set; }
    public decimal SumDeliveryFee { get; set; }
    public decimal PayableDeliveryFee { get; set; }
}
