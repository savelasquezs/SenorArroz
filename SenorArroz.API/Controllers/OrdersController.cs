using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;

    public OrdersController(
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
    /// Obtiene una lista paginada de pedidos.
    /// Por defecto filtra las órdenes del día actual.
    /// </summary>
    /// <param name="page">Número de página (default: 1)</param>
    /// <param name="pageSize">Tamaño de página (default: 10)</param>
    /// <param name="sortBy">Campo por el cual ordenar</param>
    /// <param name="sortOrder">Orden ascendente (asc) o descendente (desc)</param>
    /// <param name="branchId">ID de sucursal para filtrar (solo superadmin)</param>
    /// <param name="fromDate">Fecha inicial del filtro (default: inicio del día actual)</param>
    /// <param name="toDate">Fecha final del filtro (default: fin del día actual)</param>
    /// <param name="forKitchen">Si es true, solo estados de cocina y oculta pedidos programados hasta prepareAt; reservas «del día» usan calendario Colombia</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortOrder = "asc",
        [FromQuery] int? branchId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] bool forKitchen = false)
    {
        var query = new GetOrdersQuery
        {
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder,
            BranchId = branchId,
            FromDate = fromDate,
            ToDate = toDate,
            ForKitchen = forKitchen
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene un pedido por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDto>> GetOrder(int id)
    {
        var query = new GetOrderByIdQuery { Id = id };
        var result = await _mediator.Send(query);
        
        if (result == null)
            return NotFound();
            
        return Ok(result);
    }

    /// <summary>
    /// Obtiene un pedido con todos los detalles (productos, pagos, etc.)
    /// </summary>
    [HttpGet("{id}/details")]
    public async Task<ActionResult<OrderWithDetailsDto>> GetOrderWithDetails(int id)
    {
        var query = new GetOrderWithDetailsQuery { Id = id };
        var result = await _mediator.Send(query);
        
        if (result == null)
            return NotFound();
            
        return Ok(result);
    }

    /// <summary>
    /// Obtiene pedidos por estado
    /// </summary>
    [HttpGet("by-status/{status}")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetOrdersByStatus(
        OrderStatus status,
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortOrder = "asc")
    {
        var query = new GetOrdersByStatusQuery
        {
            Status = status,
            BranchId = branchId,
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Busca pedidos con filtros avanzados
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<PagedResult<OrderDto>>> SearchOrders([FromBody] OrderSearchDto searchDto)
    {
        var query = new SearchOrdersQuery
        {
            SearchTerm = searchDto.SearchTerm,
            BranchId = searchDto.BranchId,
            CustomerId = searchDto.CustomerId,
            DeliveryManId = searchDto.DeliveryManId,
            BankId = searchDto.BankId,
            Status = searchDto.Status,
            Type = searchDto.Type,
            FromDate = searchDto.FromDate,
            ToDate = searchDto.ToDate,
            ReservedFromDate = searchDto.ReservedFromDate,
            ReservedToDate = searchDto.ReservedToDate,
            MinAmount = searchDto.MinAmount,
            MaxAmount = searchDto.MaxAmount,
            Page = searchDto.Page,
            PageSize = searchDto.PageSize,
            SortBy = searchDto.SortBy,
            SortOrder = searchDto.SortOrder,
            ExcludeFutureReservations = searchDto.ExcludeFutureReservations
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Crea un nuevo pedido
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<OrderDto>> CreateOrder([FromBody] CreateOrderDto orderDto)
    {
        var command = new CreateOrderCommand { Order = orderDto };
        var result = await _mediator.Send(command);
        
        return CreatedAtAction(nameof(GetOrder), new { id = result.Id }, result);
    }

    /// <summary>
    /// Actualiza un pedido existente
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<OrderDto>> UpdateOrder(int id, [FromBody] UpdateOrderDto orderDto)
    {
        var command = new UpdateOrderCommand
        {
            Id = id,
            Order = orderDto
        };
        
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Cambia el estado de un pedido
    /// </summary>
    [HttpPut("{id}/status")]
    [Authorize(Roles = "Admin,Superadmin,Cashier,Kitchen,Deliveryman")]
    public async Task<ActionResult<OrderDto>> ChangeOrderStatus(int id, [FromBody] ChangeOrderStatusDto statusDto)
    {
        var command = new ChangeOrderStatusCommand
        {
            Id = id,
            StatusChange = statusDto
        };
        
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Asigna un domiciliario a un pedido
    /// </summary>
    [HttpPut("{id}/assign-delivery")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<OrderDto>> AssignDeliveryMan(int id, [FromBody] AssignDeliveryManDto assignmentDto)
    {
        var command = new AssignDeliveryManCommand
        {
            Id = id,
            Assignment = assignmentDto
        };
        
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Desasigna el domiciliario de un pedido
    /// </summary>
    [HttpPut("{id}/unassign-delivery")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<OrderDto>> UnassignDeliveryMan(int id)
    {
        var command = new UnassignDeliveryManCommand { Id = id };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Marca si el efectivo pendiente ya se cobró en la sucursal (domiciliario no cobra en entrega; cuadre de caja usa el snapshot).
    /// </summary>
    [HttpPut("{id}/paid-in-store-cash")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<OrderDto>> SetPaidInStoreCash(int id, [FromBody] SetOrderPaidInStoreCashDto body)
    {
        var command = new SetOrderPaidInStoreCashCommand
        {
            OrderId = id,
            PaidInStoreCash = body.PaidInStoreCash,
            PaidInStoreCashAmount = body.PaidInStoreCashAmount
        };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Cancela un pedido (cualquier día salvo reservas con prepare_at y reserved_for: esas solo el día UTC de creación;
    /// requiere razón; elimina pagos asociados no contabilizados vía repositorio).
    /// </summary>
    [HttpPut("{id}/cancel")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<OrderDto>> CancelOrder(int id, [FromBody] CancelOrderDto cancellationDto)
    {
        var command = new CancelOrderCommand
        {
            Id = id,
            Cancellation = cancellationDto
        };
        
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Elimina un pedido
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> DeleteOrder(int id)
    {
        var command = new DeleteOrderCommand { Id = id };
        await _mediator.Send(command);
        return NoContent();
    }

    // Endpoints específicos para cocina
    /// <summary>
    /// Obtiene pedidos en preparación (para cocina)
    /// </summary>
    [HttpGet("kitchen/preparation")]
    [Authorize(Roles = "Admin,Superadmin,Kitchen")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetOrdersInPreparation(
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var query = new GetOrdersByStatusQuery
        {
            Status = OrderStatus.InPreparation,
            BranchId = branchId,
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    // Endpoints específicos para domiciliarios
    /// <summary>
    /// Obtiene pedidos listos para entrega (para domiciliarios)
    /// </summary>
    [HttpGet("delivery/ready")]
    [Authorize(Roles = "Admin,Superadmin,Deliveryman")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetReadyOrders(
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var query = new GetOrdersByStatusQuery
        {
            Status = OrderStatus.Ready,
            TypeFilter = OrderType.Delivery, // Solo mostrar pedidos de tipo Delivery
            BranchId = branchId,
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Resumen de pedidos asignados al domiciliario agrupados por sucursal (mismo criterio de fechas que el historial).
    /// <paramref name="status"/> opcional (p. ej. Delivered) para contar solo entregas.
    /// </summary>
    [HttpGet("delivery/assigned/{deliveryManId:int}/branch-summary")]
    [Authorize(Roles = "Admin,Superadmin,Deliveryman,Cashier")]
    public async Task<ActionResult<List<DeliverymanAssignedBranchSummaryDto>>> GetAssignedOrdersBranchSummary(
        int deliveryManId,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] bool includeOnsiteActiveInHistory = false)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.Equals(role, "Deliveryman", StringComparison.OrdinalIgnoreCase))
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId) || userId != deliveryManId)
                return Forbid();
        }
        else
        {
            var gate = await EnsureMayQueryDeliverymanStaffAsync(deliveryManId, branchQueryFilter: null, HttpContext.RequestAborted);
            if (gate != null) return gate;
        }

        var query = new GetDeliverymanAssignedBranchSummaryQuery
        {
            DeliveryManId = deliveryManId,
            FromDate = fromDate,
            ToDate = toDate,
            Status = status,
            IncludeOnsiteActiveInHistory = includeOnsiteActiveInHistory
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Barrios distintos donde el domiciliario tuvo pedidos en el mismo criterio de fechas que el historial
    /// (opcionalmente filtrado por sucursal y estado).
    /// </summary>
    [HttpGet("delivery/assigned/{deliveryManId:int}/neighborhoods")]
    [Authorize(Roles = "Admin,Superadmin,Deliveryman,Cashier")]
    public async Task<ActionResult<List<DeliverymanHistoryNeighborhoodDto>>> GetDeliverymanHistoryNeighborhoods(
        int deliveryManId,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? branchId = null,
        [FromQuery] OrderStatus? status = null)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.Equals(role, "Deliveryman", StringComparison.OrdinalIgnoreCase))
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId) || userId != deliveryManId)
                return Forbid();
        }
        else
        {
            var gate = await EnsureMayQueryDeliverymanStaffAsync(deliveryManId, branchId, HttpContext.RequestAborted);
            if (gate != null) return gate;
        }

        var query = new GetDeliverymanHistoryNeighborhoodsQuery
        {
            DeliveryManId = deliveryManId,
            FromDate = fromDate,
            ToDate = toDate,
            BranchId = branchId is > 0 ? branchId : null,
            Status = status
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene pedidos asignados a un domiciliario.
    /// Si se envía <paramref name="fromDate"/> y/o <paramref name="toDate"/>, filtra por <c>CreatedAt</c> en ese rango (inclusive por día calendario, interpretado en UTC).
    /// Si no se envían fechas, no se aplica filtro de fechas (útil para pedidos en ruta).
    /// <paramref name="branchId"/> (opcional): para el domiciliario, acota el historial a una sucursal (pestañas).
    /// <paramref name="status"/> (opcional): filtra por estado del pedido (p. ej. entregados en el historial).
    /// <paramref name="type"/> (opcional): filtra por tipo de pedido (p. ej. solo <c>Delivery</c> para la lista en ruta).
    /// <paramref name="includeOnsiteActiveInHistory"/> (opcional): con <paramref name="status"/> = <c>Delivered</c> y fechas,
    /// incluye también pedidos <c>Onsite</c> en <c>OnTheWay</c> del mismo domiciliario (historial unificado en la app).
    /// <paramref name="neighborhoodId"/> (opcional): filtra por barrio de la dirección del pedido.
    /// </summary>
    [HttpGet("delivery/assigned/{deliveryManId}")]
    [Authorize(Roles = "Admin,Superadmin,Deliveryman,Cashier")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetAssignedOrders(
        int deliveryManId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? branchId = null,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] OrderType? type = null,
        [FromQuery] bool includeOnsiteActiveInHistory = false,
        [FromQuery] int? neighborhoodId = null)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.Equals(role, "Deliveryman", StringComparison.OrdinalIgnoreCase))
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId) || userId != deliveryManId)
                return Forbid();
        }
        else
        {
            var gate = await EnsureMayQueryDeliverymanStaffAsync(deliveryManId, branchId, HttpContext.RequestAborted);
            if (gate != null) return gate;
        }

        DateTime? fromUtc = null;
        DateTime? toUtc = null;
        if (fromDate.HasValue || toDate.HasValue)
        {
            var from = (fromDate ?? toDate)!.Value.Date;
            var to = (toDate ?? fromDate)!.Value.Date;
            (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(from, to);
        }

        var query = new SearchOrdersQuery
        {
            DeliveryManId = deliveryManId,
            BranchId = branchId is > 0 ? branchId : null,
            Status = status,
            Type = type,
            IncludeOnsiteActiveInAssignedHistory = includeOnsiteActiveInHistory,
            NeighborhoodId = neighborhoodId is > 0 ? neighborhoodId : null,
            Page = page,
            PageSize = pageSize,
            FromDate = fromUtc,
            ToDate = toUtc,
            SortBy = "CreatedAt",
            SortOrder = "desc"
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    // Endpoints para reservas
    /// <summary>
    /// Obtiene reservas para una fecha específica
    /// </summary>
    [HttpGet("reservations/date")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetReservationsForDate(
        [FromQuery] DateTime date,
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var query = new SearchOrdersQuery
        {
            Type = OrderType.Reservation,
            FromDate = date.Date,
            ToDate = date.Date.AddDays(1).AddTicks(-1),
            BranchId = branchId,
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene reservas próximas
    /// </summary>
    [HttpGet("reservations/upcoming")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetUpcomingReservations(
        [FromQuery] int? branchId = null,
        [FromQuery] int hours = 24,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var query = new SearchOrdersQuery
        {
            Type = OrderType.Reservation,
            FromDate = _clock.UtcNow,
            ToDate = _clock.UtcNow.AddHours(hours),
            BranchId = branchId,
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    // Endpoints específicos para domiciliarios - Autoasignación
    /// <summary>
    /// Obtiene pedidos disponibles para autoasignación (solo para domiciliarios)
    /// </summary>
    [HttpGet("delivery/available")]
    [Authorize(Roles = "Deliveryman")]
    public async Task<ActionResult<List<OrderDto>>> GetAvailableOrdersForDelivery(
        [FromQuery] int? branchId = null)
    {
        var query = new GetAvailableOrdersForDeliveryQuery
        {
            BranchId = branchId
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Autoasigna pedidos a sí mismo (solo para domiciliarios)
    /// </summary>
    [HttpPost("delivery/self-assign")]
    [Authorize(Roles = "Deliveryman")]
    public async Task<ActionResult<List<OrderDto>>> SelfAssignOrders([FromBody] SelfAssignOrdersDto request)
    {
        var command = new SelfAssignOrdersCommand
        {
            OrderIds = request.OrderIds,
            Password = request.Password
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Admin/cajero consultando datos de un domiciliario por id de usuario: debe ser rol domiciliario y de la misma sucursal.
    /// Superadmin sin restricción. No usar cuando el caller es domiciliario (validación propia).
    /// </summary>
    /// <param name="branchQueryFilter">Si viene &gt; 0, debe coincidir con la sucursal del usuario autenticado.</param>
    private async Task<ActionResult?> EnsureMayQueryDeliverymanStaffAsync(
        int deliveryManUserId,
        int? branchQueryFilter,
        CancellationToken cancellationToken)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        if (Roles.IsSuperadmin(role))
            return null;

        if (Roles.IsDeliveryman(role))
            return null;

        var dm = await _userRepository.GetByIdAsync(deliveryManUserId, cancellationToken);
        if (dm == null)
            return NotFound();
        if (dm.Role != UserRole.Deliveryman)
            return Forbid();
        if (dm.BranchId != _currentUser.BranchId)
            return Forbid();
        if (branchQueryFilter is > 0 && branchQueryFilter.Value != _currentUser.BranchId)
            return Forbid();

        return null;
    }
}
