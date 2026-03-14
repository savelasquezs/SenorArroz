using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.CashRegister.Commands;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Application.Features.CashRegister.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/cash-register")]
[Authorize(Roles = "Admin,Superadmin,Cashier")]
public class CashRegisterController : ControllerBase
{
    private readonly IMediator _mediator;

    public CashRegisterController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Obtiene el último cuadre de caja de una sucursal
    /// </summary>
    [HttpGet("last-closure")]
    public async Task<ActionResult<CashClosureDto>> GetLastClosure([FromQuery] int? branchId = null)
    {
        var result = await _mediator.Send(new GetLastClosureQuery { BranchId = branchId });
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Calcula los valores esperados para el cuadre actual (efectivo y bancos)
    /// </summary>
    [HttpGet("expected")]
    public async Task<ActionResult<CashRegisterExpectedDto>> GetExpected([FromQuery] int? branchId = null)
    {
        var result = await _mediator.Send(new GetCashRegisterExpectedQuery { BranchId = branchId });
        return Ok(result);
    }

    /// <summary>
    /// Guarda el cuadre de caja
    /// </summary>
    [HttpPost("close")]
    public async Task<ActionResult<CashClosureDto>> Close([FromBody] CloseCashRegisterDto dto, [FromQuery] int? branchId = null)
    {
        try
        {
            var result = await _mediator.Send(new CloseCashRegisterCommand { BranchId = branchId, Dto = dto });
            return CreatedAtAction(nameof(GetClosureById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Obtiene el historial paginado de cuadres
    /// </summary>
    [HttpGet("closures")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<PagedResult<CashClosureDto>>> GetClosures(
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _mediator.Send(new GetClosuresQuery { BranchId = branchId, Page = page, PageSize = pageSize });
        return Ok(result);
    }

    /// <summary>
    /// Obtiene el detalle de un cuadre por ID
    /// </summary>
    [HttpGet("closures/{id:int}")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<CashClosureDto>> GetClosureById(int id)
    {
        var result = await _mediator.Send(new GetClosureByIdQuery { Id = id });
        if (result == null) return NotFound();
        return Ok(result);
    }
}
