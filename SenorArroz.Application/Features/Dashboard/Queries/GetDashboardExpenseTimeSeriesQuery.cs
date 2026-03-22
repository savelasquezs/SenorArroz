using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardExpenseTimeSeriesQuery : IRequest<DashboardExpenseTimeSeriesResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }
    public int? CategoryId { get; set; }
    public int? ExpenseId { get; set; }
    /// <summary><c>day</c> o <c>month</c>. Vacío = automático según amplitud del rango.</summary>
    public string? Granularity { get; set; }
}
