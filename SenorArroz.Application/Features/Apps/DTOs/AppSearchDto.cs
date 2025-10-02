// SenorArroz.Application/Features/Apps/DTOs/AppSearchDto.cs
namespace SenorArroz.Application.Features.Apps.DTOs;

public class AppSearchDto
{
    public string? Name { get; set; }
    public int? BankId { get; set; }
    public int? BranchId { get; set; } // Solo usado por superadmin
    public bool? Active { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; } = "name";
    public string? SortOrder { get; set; } = "asc";
}
