using MediatR;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardCategoryWeightsQuery : IRequest<DashboardCategoryWeightsResponseDto>
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }

    /// <summary>day, month o year — buckets de la serie temporal.</summary>
    public string Granularity { get; set; } = "day";

    /// <summary>Si tiene valor, <c>Evolution</c> para esa categoría; si no, <c>EvolutionsByCategory</c> (todas).</summary>
    public int? CategoryId { get; set; }
}
