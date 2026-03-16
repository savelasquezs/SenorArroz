using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.DeliverymanAdvances.Commands;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Application.Features.Deliverymen.Queries;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/deliverymen")]
[Authorize]
public class DeliverymanController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeliverymanController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Resumen completo del día: domiciliarios con estadísticas + lista de abonos.
    /// </summary>
    /// <param name="date">Fecha en YYYY-MM-DD (por defecto: día actual)</param>
    /// <param name="fromDate">Inicio del rango (prioridad sobre date)</param>
    /// <param name="toDate">Fin del rango (prioridad sobre date)</param>
    /// <param name="branchId">ID sucursal (solo superadmin)</param>
    [HttpGet("daily-overview")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<object>> GetDailyOverview(
        [FromQuery] DateTime? date = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? branchId = null)
    {
        var query = new GetDailyOverviewQuery
        {
            Date = date,
            FromDate = fromDate,
            ToDate = toDate,
            BranchId = branchId
        };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Detalle de un domiciliario: stats + pedidos (para modal de detalle).
    /// </summary>
    [HttpGet("{id}/day-summary")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<object>> GetDaySummary(
        int id,
        [FromQuery] DateTime? date = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        var query = new GetDeliverymanDaySummaryQuery
        {
            DeliverymanId = id,
            Date = date,
            FromDate = fromDate,
            ToDate = toDate
        };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Pedidos de un domiciliario (para modal de pedidos).
    /// </summary>
    [HttpGet("{id}/orders")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<PagedResult<object>>> GetOrders(
        int id,
        [FromQuery] DateTime? date = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var (from, to) = ResolveDateRange(date, fromDate, toDate);
        var query = new SearchOrdersQuery
        {
            DeliveryManId = id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Delivery,
            FromDate = from,
            ToDate = to,
            Page = page,
            PageSize = pageSize,
            SortBy = "CreatedAt",
            SortOrder = "desc"
        };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Lista de abonos del período (opcional, si se prefiere separado de daily-overview).
    /// </summary>
    [HttpGet("advances")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<object>> GetAdvances(
        [FromQuery] DateTime? date = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        var overview = await _mediator.Send(new GetDailyOverviewQuery
        {
            Date = date,
            FromDate = fromDate,
            ToDate = toDate
        });
        return Ok(new { advances = overview.Advances });
    }

    /// <summary>
    /// Lista de domiciliarios con pedidos de delivery en el día actual (o rango dado).
    /// Usado, por ejemplo, para registrar abonos desde otros módulos.
    /// </summary>
    [HttpGet("with-orders-today")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<IEnumerable<object>>> GetDeliverymenWithOrdersToday(
        [FromQuery] DateTime? date = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? branchId = null)
    {
        var overview = await _mediator.Send(new GetDailyOverviewQuery
        {
            Date = date,
            FromDate = fromDate,
            ToDate = toDate,
            BranchId = branchId
        });

        var deliverymenWithOrders = overview.Deliverymen
            .Where(d => d.OrdersCount > 0)
            .Select(d => new
            {
                id = d.DeliverymanId,
                name = d.DeliverymanName,
                ordersCount = d.OrdersCount,
                currentBalance = d.CurrentBalance
            });

        return Ok(deliverymenWithOrders);
    }

    /// <summary>
    /// Crear abono para un domiciliario.
    /// </summary>
    [HttpPost("{id}/advances")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<object>> CreateAdvance(int id, [FromBody] CreateDeliverymanAdvanceDto dto)
    {
        var command = new CreateAdvanceCommand
        {
            Advance = new CreateDeliverymanAdvanceDto
            {
                DeliverymanId = id,
                Amount = dto.Amount,
                Notes = dto.Notes
            }
        };
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetDaySummary), new { id }, result);
    }

    /// <summary>
    /// Actualizar un abono.
    /// </summary>
    [HttpPut("{id}/advances/{advanceId}")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<object>> UpdateAdvance(int id, int advanceId, [FromBody] UpdateDeliverymanAdvanceDto dto)
    {
        var command = new UpdateAdvanceCommand
        {
            Id = advanceId,
            Advance = dto
        };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Eliminar un abono.
    /// </summary>
    [HttpDelete("{id}/advances/{advanceId}")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult> DeleteAdvance(int id, int advanceId)
    {
        await _mediator.Send(new DeleteAdvanceCommand { Id = advanceId });
        return NoContent();
    }

    private static (DateTime? from, DateTime? to) ResolveDateRange(DateTime? date, DateTime? fromDate, DateTime? toDate)
    {
        static DateTime ToUtc(DateTime d) =>
            d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc);

        if (fromDate.HasValue && toDate.HasValue)
        {
            var from = ToUtc(fromDate.Value);
            var to = ToUtc(toDate.Value);
            if (to.TimeOfDay == TimeSpan.Zero)
                to = to.Date.AddDays(1).AddTicks(-1);
            return (from, to);
        }
        var d = date?.Date ?? DateTime.UtcNow.Date;
        return (ToUtc(d), ToUtc(d.AddDays(1).AddTicks(-1)));
    }
}
