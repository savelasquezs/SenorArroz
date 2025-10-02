// SenorArroz.Application/Features/Banks/DTOs/BankSearchDto.cs
namespace SenorArroz.Application.Features.Banks.DTOs;

public class BankSearchDto
{
    public string? Name { get; set; }
    public int? BranchId { get; set; } // Solo usado por superadmin
    public bool? Active { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; } = "name";
    public string? SortOrder { get; set; } = "asc";
}
