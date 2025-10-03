using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.DTOs;

public class OrderWithDetailsDto : OrderDto
{
    public List<OrderDetailDto> OrderDetails { get; set; } = new();
    public List<BankPaymentDto> BankPayments { get; set; } = new();
    public List<AppPaymentDto> AppPayments { get; set; } = new();
}

public class BankPaymentDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AppPaymentDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsSetted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
