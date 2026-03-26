using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardDeliveryQuery : IRequest<DashboardDeliveryResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }

    /// <summary>Solo superadmin; el resto usa sucursal del token (excepto rol domiciliario en flujos internos).</summary>
    public int? BranchId { get; set; }

    /// <summary>Opcional. Filtra pedidos domicilio entregados y ventas asociadas a ese repartidor.</summary>
    public int? DeliveryManId { get; set; }
}
