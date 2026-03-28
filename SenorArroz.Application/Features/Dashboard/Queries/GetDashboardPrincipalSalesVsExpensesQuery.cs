using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardPrincipalSalesVsExpensesQuery
    : IRequest<DashboardPrincipalSalesVsExpensesResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }

    /// <summary><c>day</c>, <c>month</c> o <c>year</c>.</summary>
    public string Granularity { get; set; } = "day";
}
