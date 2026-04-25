using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Orders.Queries;

public class SearchOrdersQuery : IRequest<PagedResult<OrderDto>>
{
    public string? SearchTerm { get; set; }
    public int? BranchId { get; set; }
    public int? CustomerId { get; set; }
    public int? DeliveryManId { get; set; }
    public int? BankId { get; set; }

    /// <summary>Solo pedidos con al menos un pago por app para esta app.</summary>
    public int? AppId { get; set; }

    /// <summary>Si true, exige al menos un pago por app no liquidado (opcionalmente filtrado por <see cref="AppId"/>).</summary>
    public bool AppPaymentsUnsettledOnly { get; set; }

    /// <summary>Solo dígitos: prefijo del total del pedido como texto decimal.</summary>
    public string? TotalDigitsPrefix { get; set; }

    /// <summary>Filtro por barrio de la dirección del pedido (Address.NeighborhoodId).</summary>
    public int? NeighborhoodId { get; set; }
    public OrderStatus? Status { get; set; }
    public OrderType? Type { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    /// <summary>Filtra por reservedFor (fecha del evento), no por createdAt</summary>
    public DateTime? ReservedFromDate { get; set; }
    public DateTime? ReservedToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; }
    public string SortOrder { get; set; } = "asc";
    /// <summary>Si true, excluye reservas cuyo reservedFor es posterior a hoy (fin del día)</summary>
    public bool ExcludeFutureReservations { get; set; } = false;

    /// <summary>
    /// Historial del domiciliario: junto con <see cref="Status"/> = Delivered incluye también pedidos
    /// <c>Onsite</c> en <c>OnTheWay</c> asignados a ese domiciliario (mismos filtros de fecha/sucursal).
    /// </summary>
    public bool IncludeOnsiteActiveInAssignedHistory { get; set; }
}
