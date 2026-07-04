using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardSalesProductsQuery : IRequest<DashboardSalesProductsResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }
    public int Top { get; set; } = 10;
    public SalesProductsGroupBy GroupBy { get; set; } = SalesProductsGroupBy.Product;
    public int? DayOfWeek { get; set; }
}
