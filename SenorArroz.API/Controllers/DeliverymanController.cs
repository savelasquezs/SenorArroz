using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.DeliverymanAdvances.Commands;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Application.Features.DeliverymanAdvances.Queries;
using SenorArroz.Application.Features.Deliverymen.Commands;
using SenorArroz.Application.Features.Deliverymen.DTOs;
using SenorArroz.Application.Features.Deliverymen.Queries;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/deliverymen")]
[Authorize]
public class DeliverymanController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;

    public DeliverymanController(
        IMediator mediator,
        IClock clock,
        IUserRepository userRepository,
        ICurrentUser currentUser)
    {
        _mediator = mediator;
        _clock = clock;
        _userRepository = userRepository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Estado mínimo del día para el domiciliario autenticado (p. ej. día bloqueado tras liquidación total).
    /// </summary>
    [HttpGet("me/day-state")]
    [Authorize(Roles = "Deliveryman")]
    public async Task<ActionResult<MyDeliverymanDayStateDto>> GetMyDayState([FromQuery] string? date = null)
    {
        var result = await _mediator.Send(new GetMyDeliverymanDayStateQuery { Date = date });
        return Ok(result);
    }

    /// <summary>
    /// Abonos / préstamos del domiciliario autenticado en el rango indicado.
    /// </summary>
    [HttpGet("me/advances")]
    [Authorize(Roles = "Deliveryman")]
    public async Task<ActionResult<object>> GetMyAdvances(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        var list = await _mediator.Send(new GetMyDeliverymanAdvancesQuery
        {
            FromDate = fromDate,
            ToDate = toDate
        });
        return Ok(new { advances = list });
    }

    /// <summary>
    /// Mismo detalle que <c>GET {id}/day-summary</c>, pero solo para el usuario del token.
    /// </summary>
    [HttpGet("me/day-summary")]
    [Authorize(Roles = "Deliveryman")]
    public async Task<ActionResult<object>> GetMyDaySummary(
        [FromQuery] DateTime? date = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] decimal? baseAmount = null)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return Unauthorized();

        var query = new GetDeliverymanDaySummaryQuery
        {
            DeliverymanId = id,
            Date = date,
            FromDate = fromDate,
            ToDate = toDate,
            BaseAmount = baseAmount
        };
        var result = await _mediator.Send(query);
        return Ok(result);
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
        [FromQuery] DateTime? toDate = null,
        [FromQuery] decimal? baseAmount = null)
    {
        var query = new GetDeliverymanDaySummaryQuery
        {
            DeliverymanId = id,
            Date = date,
            FromDate = fromDate,
            ToDate = toDate,
            BaseAmount = baseAmount
        };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Liquidación del día: crea abonos (efectivo / transferencia / gasto) y actualiza estado del día.
    /// </summary>
    [HttpPost("{id}/settle-day")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<SettleDeliverymanDayResultDto>> SettleDay(int id, [FromBody] SettleDeliverymanDayDto dto)
    {
        var result = await _mediator.Send(new SettleDeliverymanDayCommand
        {
            DeliverymanId = id,
            Settlement = dto
        });
        return Ok(result);
    }

    /// <summary>
    /// Desbloquea la tarjeta del domiciliario para el día (tras liquidación total).
    /// </summary>
    [HttpPost("{id}/unlock-day")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult> UnlockDay(int id, [FromQuery] string date)
    {
        await _mediator.Send(new UnlockDeliverymanDayCommand { DeliverymanId = id, Date = date });
        return NoContent();
    }

    /// <summary>
    /// Pedidos de un domiciliario (para modal de pedidos).
    /// </summary>
    [HttpGet("{id}/orders")]
    [Authorize(Roles = "Superadmin,Admin,Cashier")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetOrders(
        int id,
        [FromQuery] DateTime? date = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var (from, to) = ResolveDateRange(date, fromDate, toDate);
        const int fetchCap = 5000;
        var qDelivery = new SearchOrdersQuery
        {
            DeliveryManId = id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Delivery,
            FromDate = from,
            ToDate = to,
            Page = 1,
            PageSize = fetchCap,
            SortBy = "CreatedAt",
            SortOrder = "desc"
        };
        var qOnsite = new SearchOrdersQuery
        {
            DeliveryManId = id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Onsite,
            FromDate = from,
            ToDate = to,
            Page = 1,
            PageSize = fetchCap,
            SortBy = "CreatedAt",
            SortOrder = "desc"
        };
        var delivery = await _mediator.Send(qDelivery);
        var onsite = await _mediator.Send(qOnsite);
        var combined = delivery.Items
            .Concat(onsite.Items)
            .GroupBy(o => o.Id)
            .Select(g => g.First())
            .OrderByDescending(o => o.CreatedAt)
            .ToList();
        var total = delivery.TotalCount + onsite.TotalCount;
        var skip = (page - 1) * pageSize;
        var pageItems = combined.Skip(skip).Take(pageSize).ToList();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(pageSize, 1)));
        return Ok(new PagedResult<OrderDto>
        {
            Items = pageItems,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
        });
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
    /// Lista de domiciliarios con pedidos entregados en el día (o rango): delivery y onsite con domiciliario asignado.
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
                Notes = dto.Notes,
                PaymentMethod = dto.PaymentMethod,
                BankId = dto.BankId,
                ExpenseHeaderId = dto.ExpenseHeaderId
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

    // ─── Device tokens (FCM push) ────────────────────────────────────────────

    /// <summary>
    /// Registra (o actualiza) el token FCM del dispositivo del domiciliario autenticado.
    /// </summary>
    [HttpPost("me/device-token")]
    [Authorize(Roles = "Deliveryman")]
    public async Task<ActionResult> RegisterDeviceToken([FromBody] RegisterDeviceTokenRequest request)
    {
        await _mediator.Send(new RegisterDeviceTokenCommand
        {
            Token = request.Token,
            Platform = request.Platform ?? "android",
        });
        return NoContent();
    }

    /// <summary>
    /// Elimina el token FCM del dispositivo (al cerrar sesión).
    /// </summary>
    [HttpDelete("me/device-token")]
    [Authorize(Roles = "Deliveryman")]
    public async Task<ActionResult> RemoveDeviceToken([FromBody] RemoveDeviceTokenRequest request)
    {
        await _mediator.Send(new RemoveDeviceTokenCommand { Token = request.Token });
        return NoContent();
    }

    // ─── GPS Location ────────────────────────────────────────────────────────

    /// <summary>
    /// Registra la ubicación GPS del domiciliario autenticado.
    /// Solo se guarda si tiene una ruta activa con pedidos "on the way".
    /// </summary>
    [HttpPost("location")]
    [Authorize(Roles = "Deliveryman")]
    public async Task<ActionResult> RecordLocation([FromBody] RecordLocationRequest request)
    {
        await _mediator.Send(new RecordLocationCommand
        {
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RecordedAt = request.RecordedAt,
        });
        return NoContent();
    }

    /// <summary>
    /// Retorna la última ubicación registrada de un domiciliario (para fallback de polling).
    /// </summary>
    [HttpGet("{id}/last-location")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<DeliverymanLastLocationDto>> GetLastLocation(int id)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        if (!Roles.IsSuperadmin(role))
        {
            var dm = await _userRepository.GetByIdAsync(id, HttpContext.RequestAborted);
            if (dm == null)
                return NotFound();
            if (dm.Role != UserRole.Deliveryman)
                return Forbid();
            if (dm.BranchId != _currentUser.BranchId)
                return Forbid();
        }

        var result = await _mediator.Send(new GetDeliverymanLastLocationQuery { DeliverymanId = id });
        if (result is null)
            return NotFound();
        return Ok(result);
    }

    private (DateTime? from, DateTime? to) ResolveDateRange(DateTime? date, DateTime? fromDate, DateTime? toDate)
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
        var d = date?.Date ?? _clock.UtcNow.Date;
        return (ToUtc(d), ToUtc(d.AddDays(1).AddTicks(-1)));
    }
}
