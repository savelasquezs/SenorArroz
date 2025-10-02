// SenorArroz.Application/Features/BankPayments/DTOs/BankPaymentSearchDto.cs
namespace SenorArroz.Application.Features.BankPayments.DTOs;

public class BankPaymentSearchDto
{
    public int? OrderId { get; set; }
    public int? BankId { get; set; }
    public int? BranchId { get; set; } // Solo usado por superadmin
    public bool? Verified { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; } = "createdAt";
    public string? SortOrder { get; set; } = "desc";
}
