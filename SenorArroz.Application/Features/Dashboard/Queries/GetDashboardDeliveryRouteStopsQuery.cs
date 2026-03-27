using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardDeliveryRouteStopsQuery : IRequest<DashboardDeliveryRouteStopsResponseDto>
{
    public int RouteId { get; set; }
    /// <summary>Filtro de sucursal (superadmin / domiciliario con pestaña); null = sin filtro extra.</summary>
    public int? BranchId { get; set; }
}
