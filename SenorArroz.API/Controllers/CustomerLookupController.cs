using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Application.Features.Customers.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

/// <summary>Read-only autocomplete and on-selection hydration; existing customer APIs remain compatible.</summary>
[ApiController, Authorize, Route("api/customers/lookup")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerLookupController(IMediator mediator, IBranchContext branchContext) : ControllerBase
{
    // An extra static segment avoids the legacy /customers/{id} route during staged rollout.
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<CustomerSuggestionPageDto>>> Search(
        [FromQuery] GetCustomerSuggestionsQuery query, CancellationToken cancellationToken)
    {
        query.BranchId = branchContext.RequireBranch(query.BranchId);
        var result = await mediator.Send(query, cancellationToken);
        return Ok(ApiResponse<CustomerSuggestionPageDto>.SuccessResponse(result, "Clientes encontrados"));
    }

    // Reuse the authorized, tenant-scoped detail handler, including addresses and loyalty.
    // The MVC cancellation token flows through MediatR to all EF reads in that handler.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> Select(int id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCustomerByIdQuery { Id = id }, cancellationToken);
        if (result is null || !result.Active)
            return NotFound(ApiResponse<CustomerDto>.ErrorResponse("Cliente no disponible"));
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result, "Cliente obtenido"));
    }
}
