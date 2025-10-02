// SenorArroz.Application/Features/AppPayments/DTOs/UnsettledAppPaymentsDto.cs
namespace SenorArroz.Application.Features.AppPayments.DTOs;

public class UnsettledAppPaymentsDto
{
    public int? AppId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
