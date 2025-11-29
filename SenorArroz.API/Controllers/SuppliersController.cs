using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SenorArroz.Application.Features.Suppliers.Commands;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Application.Features.Suppliers.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly IMediator _mediator;

    public SuppliersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierDto>>> GetSuppliers(
        [FromQuery] string? search = null,
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = "name",
        [FromQuery] string sortOrder = "asc")
    {
        var query = new GetSuppliersQuery
        {
            Search = search,
            BranchId = branchId,
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("by-branch")]
    public async Task<ActionResult<List<SupplierDto>>> GetSuppliersByBranch([FromQuery] int? branchId = null)
    {
        var query = new GetSuppliersByBranchQuery { BranchId = branchId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SupplierDto>> GetSupplierById(int id)
    {
        var query = new GetSupplierByIdQuery { Id = id };
        var result = await _mediator.Send(query);
        if (result == null)
        {
            return NotFound();
        }
        return Ok(result);
    }

    [HttpGet("{id}/expenses")]
    public async Task<ActionResult<List<SupplierExpenseDto>>> GetSupplierExpenses(int id)
    {
        var query = new GetSupplierExpensesQuery { SupplierId = id };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<SupplierDto>> CreateSupplier([FromBody] CreateSupplierDto dto, [FromQuery] int? branchId = null)
    {
        var command = new CreateSupplierCommand
        {
            Supplier = dto,
            BranchId = branchId
        };

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetSuppliers), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<SupplierDto>> UpdateSupplier(int id, [FromBody] UpdateSupplierDto dto)
    {
        var command = new UpdateSupplierCommand
        {
            Id = id,
            Supplier = dto
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult> DeleteSupplier(int id)
    {
        var command = new DeleteSupplierCommand { Id = id };
        var result = await _mediator.Send(command);

        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }
}


