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
    /// Abono o descarga de efectivo contra el banco tipo Caja Mayor Efectivo (sin usar transferencias).
    /// </summary>
    [HttpPost("cash-vault-movements")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<CashVaultMovementDto>> CreateCashVaultMovement(
        [FromBody] CreateCashVaultMovementDto dto,
        [FromQuery] int? branchId = null)
    {
        var result = await _mediator.Send(new CreateCashVaultMovementCommand { BranchId = branchId, Dto = dto });
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Historial paginado de movimientos de Caja Mayor Efectivo.
    /// </summary>
    [HttpGet("cash-vault-movements")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<PagedResult<CashVaultMovementDto>>> GetCashVaultMovements(
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _mediator.Send(new GetCashVaultMovementsQuery
        {
            BranchId = branchId,
            Page = page,
            PageSize = pageSize
        });
        return Ok(result);
    }

    /// <summary>
    /// Pedidos domicilio listos o en camino (para préstamo anticipado / excepción cuadre).
    /// </summary>
    [HttpGet("delivery-advance/orders")]
    public async Task<ActionResult<List<DeliveryAdvanceOrderRowDto>>> GetDeliveryAdvanceOrders([FromQuery] int? branchId = null)
    {
        var result = await _mediator.Send(new GetDeliveryAdvanceOrdersQuery { BranchId = branchId });
        return Ok(result);
    }

    /// <summary>
    /// Domiciliarios liquidados hoy (modo total + bloqueado): tienen el dinero fuera de caja esperada.
    /// </summary>
    [HttpGet("delivery-advance/liquidated-deliverymen")]
    public async Task<ActionResult<List<LiquidatedDeliverymanOptionDto>>> GetLiquidatedDeliverymen([FromQuery] int? branchId = null)
    {
        var result = await _mediator.Send(new GetLiquidatedFullBlockedDeliverymenQuery { BranchId = branchId });
        return Ok(result);
    }

    /// <summary>
    /// Lista préstamos informales de la sucursal (activos por defecto).
    /// </summary>
    [HttpGet("informal-loans")]
    public async Task<ActionResult<List<BranchInformalLoanDto>>> GetInformalLoans(
        [FromQuery] int? branchId = null,
        [FromQuery] string scope = "active")
    {
        var result = await _mediator.Send(new GetBranchInformalLoansQuery { BranchId = branchId, Scope = scope });
        return Ok(result);
    }

    /// <summary>
    /// Registra un préstamo informal (sin cerrar caja).
    /// </summary>
    [HttpPost("informal-loans")]
    public async Task<ActionResult<BranchInformalLoanDto>> CreateInformalLoan(
        [FromBody] CreateBranchInformalLoanDto dto,
        [FromQuery] int? branchId = null)
    {
        try
        {
            var result = await _mediator.Send(new CreateBranchInformalLoanCommand { BranchId = branchId, Dto = dto });
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Actualiza concepto y monto de un prestamo informal.
    /// </summary>
    [HttpPut("informal-loans/{id:int}")]
    public async Task<ActionResult<BranchInformalLoanDto>> UpdateInformalLoan(
        int id,
        [FromBody] UpdateBranchInformalLoanDto dto,
        [FromQuery] int? branchId = null)
    {
        try
        {
            var result = await _mediator.Send(new UpdateBranchInformalLoanCommand
            {
                Id = id,
                BranchId = branchId,
                Dto = dto
            });
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Da de baja logica a un prestamo informal.
    /// </summary>
    [HttpPost("informal-loans/{id:int}/deactivate")]
    public async Task<ActionResult<BranchInformalLoanDto>> DeactivateInformalLoan(
        int id,
        [FromBody] DeactivateBranchInformalLoanDto? dto,
        [FromQuery] int? branchId = null)
    {
        try
        {
            var result = await _mediator.Send(new DeactivateBranchInformalLoanCommand
            {
                Id = id,
                BranchId = branchId,
                Dto = dto ?? new DeactivateBranchInformalLoanDto()
            });
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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

    [HttpGet("closures/{id:int}/audit-summary")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<CashClosureAuditSummaryDto>> GetClosureAuditSummary(int id)
    {
        var result = await _mediator.Send(new GetClosureAuditSummaryQuery { Id = id });
        if (result == null) return NotFound();
        return Ok(result);
    }
}
