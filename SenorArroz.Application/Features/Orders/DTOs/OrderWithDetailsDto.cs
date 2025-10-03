using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Application.Features.AppPayments.DTOs;

namespace SenorArroz.Application.Features.Orders.DTOs;

public class OrderWithDetailsDto : OrderDto
{
    public List<OrderDetailDto> OrderDetails { get; set; } = new();
    public List<BankPaymentDto> BankPayments { get; set; } = new();
    public List<AppPaymentDto> AppPayments { get; set; } = new();
}
