using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Obtiene una lista paginada de pedidos
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortOrder = "asc",
        [FromQuery] int? branchId = null)
    {
        var query = new GetOrdersQuery
        {
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder,
            BranchId = branchId
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
            Status = searchDto.Status,
            Type = searchDto.Type,
            FromDate = searchDto.FromDate,
            ToDate = searchDto.ToDate,
            MinAmount = searchDto.MinAmount,
            MaxAmount = searchDto.MaxAmount,
            Page = searchDto.Page,
            PageSize = searchDto.PageSize,
            SortBy = searchDto.SortBy,
            SortOrder = searchDto.SortOrder
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
    /// Cancela un pedido
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
            BranchId = branchId,
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene pedidos asignados a un domiciliario
    /// </summary>
    [HttpGet("delivery/assigned/{deliveryManId}")]
    [Authorize(Roles = "Admin,Superadmin,Deliveryman")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetAssignedOrders(
        int deliveryManId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var query = new SearchOrdersQuery
        {
            DeliveryManId = deliveryManId,
            Page = page,
            PageSize = pageSize
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
            FromDate = DateTime.UtcNow,
            ToDate = DateTime.UtcNow.AddHours(hours),
            BranchId = branchId,
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }
}
