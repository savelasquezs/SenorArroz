using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardMainQuery : IRequest<DashboardMainResponseDto>
{
    /// <summary>
    /// Solo aplica para superadmin; el handler fuerza sucursal del token para el resto.
    /// </summary>
    public int? BranchId { get; set; }

    /// <summary>
    /// Máximo de filas en recent activity (acotado en handler).
    /// </summary>
    public int ActivityLimit { get; set; } = 20;

    /// <summary>
    /// Si ambos tienen valor (UTC), los KPI usan ese rango; variaciones vs periodo anterior
    /// contiguo y vs el mismo rango hace un año. Si no, se mantienen ventanas rolling 7d / 365d.
    /// </summary>
    public DateTime? KpiFromUtc { get; set; }

    public DateTime? KpiToUtc { get; set; }
}
