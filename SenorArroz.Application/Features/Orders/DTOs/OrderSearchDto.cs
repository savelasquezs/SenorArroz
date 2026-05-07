using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.DTOs;

public class OrderSearchDto
{
    public string? SearchTerm { get; set; }
    public int? BranchId { get; set; }
    public int? CustomerId { get; set; }
    public int? DeliveryManId { get; set; }
    /// <summary>Solo pedidos que tengan al menos un pago bancario asociado a este banco.</summary>
    public int? BankId { get; set; }

    /// <summary>Solo pedidos con al menos un pago por app para esta app (comportamiento análogo a <see cref="BankId"/>).</summary>
    public int? AppId { get; set; }

    /// <summary>Si true, exige al menos un <c>AppPayment</c> no liquidado; si <see cref="AppId"/> está definido, solo líneas de esa app.</summary>
    public bool AppPaymentsUnsettledOnly { get; set; }

    /// <summary>Solo dígitos: el total del pedido como entero, en texto, debe empezar por este prefijo.</summary>
    public string? TotalDigitsPrefix { get; set; }

    /// <summary>Filtro por barrio de la dirección del pedido.</summary>
    public int? NeighborhoodId { get; set; }

    /// <summary>Historial domiciliario: junto con estado entregado incluye onsite en camino.</summary>
    public bool IncludeOnsiteActiveInAssignedHistory { get; set; }
    public OrderStatus? Status { get; set; }
    public OrderType? Type { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DateTime? ReservedFromDate { get; set; }
    public DateTime? ReservedToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 150;
    public string SortBy { get; set; } = "CreatedAt";
    public string SortOrder { get; set; } = "desc";
    /// <summary>Si true, excluye reservas cuyo reservedFor es posterior a hoy (fin del día)</summary>
    public bool ExcludeFutureReservations { get; set; } = false;
}
