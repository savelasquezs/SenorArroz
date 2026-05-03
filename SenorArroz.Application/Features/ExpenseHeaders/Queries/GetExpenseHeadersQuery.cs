using MediatR;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.ExpenseHeaders.Queries;

public class GetExpenseHeadersQuery : IRequest<PagedResult<ExpenseHeaderDto>>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; }
    public string SortOrder { get; set; } = "asc";
    public int? BranchId { get; set; } // Solo para superadmin
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public List<int> SupplierIds { get; set; } = new();
    public List<string> BankNames { get; set; } = new();
    public List<string> CategoryNames { get; set; } = new();
    public string? ExpenseName { get; set; }
}


