using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Application.Features.Expenses.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Superadmin, Admin")]
public class ExpenseMenuAttributionController : ControllerBase
{
    private readonly IMediator _mediator;

    public ExpenseMenuAttributionController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Imputación de gastos de catálogo a menú por gramos vendidos en el periodo (costo estimado por gramo).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<ExpenseMenuAttributionResponseDto>>> GetAttribution(
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        [FromQuery] int? branchId = null)
    {
        var result = await _mediator.Send(new GetExpenseMenuAttributionQuery
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            BranchId = branchId,
        });

        return Ok(ApiResponse<ExpenseMenuAttributionResponseDto>.SuccessResponse(
            result,
            "Imputación calculada"));
    }

    /// <summary>
    /// Costeo por categoría de menú: gastos imputados, gramos, $/g mezclado y margen por producto (mismo periodo que imputación).
    /// </summary>
    [HttpGet("category-costing-dashboard")]
    public async Task<ActionResult<ApiResponse<MenuCategoryCostingDashboardResponseDto>>> GetCategoryCostingDashboard(
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        [FromQuery] int? branchId = null)
    {
        var result = await _mediator.Send(new GetMenuCategoryCostingDashboardQuery
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            BranchId = branchId,
        });

        return Ok(ApiResponse<MenuCategoryCostingDashboardResponseDto>.SuccessResponse(
            result,
            "Costeo por categoría calculado"));
    }
}
