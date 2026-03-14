using MediatR;
using SenorArroz.Application.Features.Deliverymen.DTOs;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetDailyOverviewQuery : IRequest<DailyOverviewDto>
{
    /// <summary>
    /// ID de sucursal (solo para superadmin).
    /// </summary>
    public int? BranchId { get; set; }

    /// <summary>
    /// Fecha en formato YYYY-MM-DD. Por defecto: día actual.
    /// </summary>
    public DateTime? Date { get; set; }

    /// <summary>
    /// Si se especifica, usa el rango en lugar de Date.
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// Si se especifica con FromDate, usa el rango.
    /// </summary>
    public DateTime? ToDate { get; set; }
}
