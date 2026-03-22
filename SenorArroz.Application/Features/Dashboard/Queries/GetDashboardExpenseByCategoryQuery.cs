using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardExpenseByCategoryQuery : IRequest<DashboardExpenseByCategoryResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }
}
