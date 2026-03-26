using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

/// <summary>
/// Métricas de domicilios del usuario autenticado (rol domiciliario). Equivale a <see cref="GetDashboardDeliveryQuery"/>
/// con <c>DeliveryManId = usuario actual</c>.
/// </summary>
public class GetDashboardDeliveryMeQuery : IRequest<DashboardDeliveryResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }

    /// <summary>Opcional. Si se envía, solo pedidos de esa sucursal; si no, todas las sucursales donde haya entregado.</summary>
    public int? BranchId { get; set; }
}
