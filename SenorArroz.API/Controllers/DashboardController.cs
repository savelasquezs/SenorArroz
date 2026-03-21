using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Queries;

namespace SenorArroz.API.Controllers;

/// <summary>
/// Métricas del dashboard por vista (sin endpoint monolítico). Prefijo: <c>/api/dashboard</c>.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "Admin,Superadmin")]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Sección Principal: KPIs, pipeline operativo y actividad reciente.
    /// Superadmin: <paramref name="branchId"/> opcional (todas las sucursales si se omite).
    /// Admin: siempre limitado a la sucursal del token.
    /// </summary>
    [HttpGet("main")]
    [ProducesResponseType(typeof(DashboardMainResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardMainResponseDto>> GetMain(
        [FromQuery] int? branchId = null,
        [FromQuery] int activityLimit = 20,
        [FromQuery(Name = "kpiFrom")] DateTime? kpiFromUtc = null,
        [FromQuery(Name = "kpiTo")] DateTime? kpiToUtc = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetDashboardMainQuery
            {
                BranchId = branchId,
                ActivityLimit = activityLimit,
                KpiFromUtc = kpiFromUtc,
                KpiToUtc = kpiToUtc,
            },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Sección Domicilios: métricas de entregas en un rango (fechas obligatorias, UTC recomendado ISO 8601).
    /// </summary>
    [HttpGet("delivery")]
    [ProducesResponseType(typeof(DashboardDeliveryResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDeliveryResponseDto>> GetDelivery(
        [FromQuery(Name = "from")] DateTime fromUtc,
        [FromQuery(Name = "to")] DateTime toUtc,
        [FromQuery] int? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetDashboardDeliveryQuery
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                BranchId = branchId,
            },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Ventas — comparación entre sucursales en el rango (pedidos no cancelados por <c>CreatedAt</c>).</summary>
    [HttpGet("sales/comparison")]
    [ProducesResponseType(typeof(DashboardSalesComparisonResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSalesComparisonResponseDto>> GetSalesComparison(
        [FromQuery(Name = "from")] DateTime fromUtc,
        [FromQuery(Name = "to")] DateTime toUtc,
        [FromQuery] int? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetDashboardSalesComparisonQuery
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                BranchId = branchId,
            },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Ventas — series temporales ventas/pedidos (día, hora del último día del <c>to</c>, mes, año).</summary>
    [HttpGet("sales/evolution")]
    [ProducesResponseType(typeof(DashboardSalesEvolutionResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSalesEvolutionResponseDto>> GetSalesEvolution(
        [FromQuery(Name = "from")] DateTime fromUtc,
        [FromQuery(Name = "to")] DateTime toUtc,
        [FromQuery] int? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetDashboardSalesEvolutionQuery
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                BranchId = branchId,
            },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Ventas — top productos por unidades y participación por recaudo (donut).</summary>
    [HttpGet("sales/products")]
    [ProducesResponseType(typeof(DashboardSalesProductsResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSalesProductsResponseDto>> GetSalesProducts(
        [FromQuery(Name = "from")] DateTime fromUtc,
        [FromQuery(Name = "to")] DateTime toUtc,
        [FromQuery] int? branchId = null,
        [FromQuery] int top = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetDashboardSalesProductsQuery
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                BranchId = branchId,
                Top = top,
            },
            cancellationToken);

        return Ok(result);
    }
}
