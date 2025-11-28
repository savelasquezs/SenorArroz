using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SenorArroz.Application.Features.ExpenseHeaders.Commands;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Application.Features.ExpenseHeaders.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpenseHeadersController : ControllerBase
{
    private readonly IMediator _mediator;

    public ExpenseHeadersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Obtiene una lista paginada de gastos.
    /// Por defecto filtra los gastos del día actual.
    /// </summary>
    /// <param name="page">Número de página (default: 1)</param>
    /// <param name="pageSize">Tamaño de página (default: 10)</param>
    /// <param name="sortBy">Campo por el cual ordenar</param>
    /// <param name="sortOrder">Orden ascendente (asc) o descendente (desc)</param>
    /// <param name="branchId">ID de sucursal para filtrar (solo superadmin)</param>
    /// <param name="fromDate">Fecha inicial del filtro (default: inicio del día actual)</param>
    /// <param name="toDate">Fecha final del filtro (default: fin del día actual)</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ExpenseHeaderDto>>> GetExpenseHeaders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortOrder = "asc",
        [FromQuery] int? branchId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        var query = new GetExpenseHeadersQuery
        {
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder,
            BranchId = branchId,
            FromDate = fromDate,
            ToDate = toDate
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene un gasto por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ExpenseHeaderDto>> GetExpenseHeader(int id)
    {
        var query = new GetExpenseHeaderByIdQuery { Id = id };
        var result = await _mediator.Send(query);
        
        if (result == null)
            return NotFound();
            
        return Ok(result);
    }

    /// <summary>
    /// Crea un nuevo gasto
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<ExpenseHeaderDto>> CreateExpenseHeader([FromBody] CreateExpenseHeaderDto dto)
    {
        var command = new CreateExpenseHeaderCommand { ExpenseHeader = dto };
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetExpenseHeader), new { id = result.Id }, result);
    }

    /// <summary>
    /// Actualiza un gasto existente
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<ExpenseHeaderDto>> UpdateExpenseHeader(int id, [FromBody] UpdateExpenseHeaderDto dto)
    {
        var command = new UpdateExpenseHeaderCommand { Id = id, ExpenseHeader = dto };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Elimina un gasto
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> DeleteExpenseHeader(int id)
    {
        var command = new DeleteExpenseHeaderCommand { Id = id };
        var result = await _mediator.Send(command);
        
        if (!result)
            return NotFound();
            
        return NoContent();
    }
}


