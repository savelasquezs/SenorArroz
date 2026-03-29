using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetMenuCategoryCostingDashboardQuery : IRequest<MenuCategoryCostingDashboardResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }
}
