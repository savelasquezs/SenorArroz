using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.ReservationDeposits.Commands;
using SenorArroz.Application.Features.ReservationDeposits.DTOs;
using SenorArroz.Application.Features.ReservationDeposits.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Superadmin,Cashier")]
public class ReservationDepositsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReservationDepositsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Registra un abono/anticipo para una reserva
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ReservationDepositDto>> Create([FromBody] CreateReservationDepositDto dto)
    {
        var command = new CreateReservationDepositCommand
        {
            OrderId = dto.OrderId,
            Amount = dto.Amount,
            IsEffective = dto.IsEffective,
            BankId = dto.BankId,
            AppId = dto.AppId,
            Notes = dto.Notes
        };

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetByOrder), new { orderId = result.OrderId }, result);
    }

    /// <summary>
    /// Actualiza el monto de un abono existente
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ReservationDepositDto>> Update(int id, [FromBody] UpdateReservationDepositDto dto)
    {
        var result = await _mediator.Send(new UpdateReservationDepositCommand
        {
            Id = id,
            Amount = dto.Amount
        });
        return Ok(result);
    }

    /// <summary>
    /// Elimina un abono registrado
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        await _mediator.Send(new DeleteReservationDepositCommand { Id = id });
        return NoContent();
    }

    /// <summary>
    /// Obtiene todos los abonos de un pedido/reserva
    /// </summary>
    [HttpGet("by-order/{orderId:int}")]
    public async Task<ActionResult<List<ReservationDepositDto>>> GetByOrder(int orderId)
    {
        var result = await _mediator.Send(new GetDepositsByOrderQuery { OrderId = orderId });
        return Ok(result);
    }

    /// <summary>
    /// Obtiene abonos paginados de una sucursal con filtros opcionales
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<PagedResult<ReservationDepositDto>>> GetPaged(
        [FromQuery] int? branchId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? orderId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetDepositsByBranchQuery
        {
            BranchId = branchId,
            FromDate = fromDate,
            ToDate = toDate,
            OrderId = orderId,
            Page = page,
            PageSize = pageSize
        });

        return Ok(result);
    }
}
