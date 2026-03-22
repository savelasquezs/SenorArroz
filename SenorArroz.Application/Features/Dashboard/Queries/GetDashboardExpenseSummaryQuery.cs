using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardExpenseSummaryQuery : IRequest<DashboardExpenseSummaryResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }
}
