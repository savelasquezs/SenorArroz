using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    /// Por defecto filtra los gastos del dia actual.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ExpenseHeaderDto>>> GetExpenseHeaders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortOrder = "asc",
        [FromQuery] int? branchId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] List<int>? supplierIds = null,
        [FromQuery] List<string>? bankNames = null,
        [FromQuery] List<string>? categoryNames = null,
        [FromQuery] string? expenseName = null)
    {
        var query = new GetExpenseHeadersQuery
        {
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder,
            BranchId = branchId,
            FromDate = fromDate,
            ToDate = toDate,
            SupplierIds = supplierIds ?? new List<int>(),
            BankNames = bankNames ?? new List<string>(),
            CategoryNames = categoryNames ?? new List<string>(),
            ExpenseName = expenseName
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ExpenseHeaderDto>> GetExpenseHeader(int id)
    {
        var query = new GetExpenseHeaderByIdQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result == null)
            return NotFound();

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<ExpenseHeaderDto>> CreateExpenseHeader([FromBody] CreateExpenseHeaderDto dto)
    {
        var command = new CreateExpenseHeaderCommand { ExpenseHeader = dto };
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetExpenseHeader), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<ExpenseHeaderDto>> UpdateExpenseHeader(int id, [FromBody] UpdateExpenseHeaderDto dto)
    {
        var command = new UpdateExpenseHeaderCommand { Id = id, ExpenseHeader = dto };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

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
