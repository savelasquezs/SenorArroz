// SenorArroz.API/Controllers/UsersController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Users.Commands;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Application.Features.Users.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Requiere autenticación para todos los endpoints
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Obtiene todos los usuarios, filtrados automáticamente por sucursal del usuario actual
    /// </summary>
    /// <param name="branchId">ID de sucursal (solo para superadmin)</param>
    /// <param name="role">Filtrar por rol (ej: Deliveryman, Cashier, etc.)</param>
    /// <param name="active">Filtrar por usuarios activos/inactivos</param>
    /// <param name="page">Número de página (default: 1)</param>
    /// <param name="pageSize">Tamaño de página (default: 10)</param>
    /// <param name="sortBy">Campo por el cual ordenar</param>
    /// <param name="sortOrder">Orden ascendente (asc) o descendente (desc)</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserDto>>> GetUsers(
        [FromQuery] int? branchId = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? active = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortOrder = "asc")
    {
        var query = new GetUsersQuery(branchId, role, active, page, pageSize, sortBy, sortOrder);
        var users = await _mediator.Send(query);
        return Ok(users);
    }

    /// <summary>
    /// Obtiene un usuario por su ID
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var query = new GetUserByIdQuery(id);
        var user = await _mediator.Send(query);
        return Ok(user);
    }

    /// <summary>
    /// Crea un nuevo usuario
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Superadmin,Admin")] // Solo admin y superadmin pueden crear usuarios
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserDto createUserDto)
    {
        var command = new CreateUserCommand(createUserDto);
        var user = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }

    /// <summary>
    /// Actualiza un usuario existente
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<UserDto>> UpdateUser(int id, [FromBody] UpdateUserDto updateUserDto)
    {
        var command = new UpdateUserCommand(id, updateUserDto);
        var user = await _mediator.Send(command);
        return Ok(user);
    }
    /// <summary>
    /// Cambiar el estado activo/inactivo de un usuario
    /// </summary>
    [HttpPut("{id:int}/toggle-status")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<UserDto>> ToggleStatus(int id)
    {
        var command = new ToggleStatusCommand(id);
        var user = await _mediator.Send(command);
        return Ok(user);
    }

    /// <summary>
    /// Elimina un usuario (soft delete)
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Superadmin")]  // Solo superadmin puede eliminar
    public async Task<ActionResult> DeleteUser(int id)
    {
        var command = new DeleteUserCommand(id);
        await _mediator.Send(command);
        return NoContent();
    }

    // ===== ENDPOINTS PARA ABONOS DE DOMICILIARIOS =====

    /// <summary>
    /// Crea un abono para un domiciliario
    /// </summary>
    [HttpPost("{id:int}/advances")]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<ActionResult<SenorArroz.Application.Features.DeliverymanAdvances.DTOs.DeliverymanAdvanceDto>> CreateAdvance(
        int id,
        [FromBody] SenorArroz.Application.Features.DeliverymanAdvances.DTOs.CreateDeliverymanAdvanceDto advanceDto)
    {
        advanceDto.DeliverymanId = id; // Override con el ID de la ruta
        var command = new SenorArroz.Application.Features.DeliverymanAdvances.Commands.CreateAdvanceCommand
        {
            Advance = advanceDto
        };
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetAdvances), new { deliverymanId = id }, result);
    }

    /// <summary>
    /// Actualiza un abono existente
    /// </summary>
    [HttpPut("{id:int}/advances/{advanceId:int}")]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<ActionResult<SenorArroz.Application.Features.DeliverymanAdvances.DTOs.DeliverymanAdvanceDto>> UpdateAdvance(
        int id,
        int advanceId,
        [FromBody] SenorArroz.Application.Features.DeliverymanAdvances.DTOs.UpdateDeliverymanAdvanceDto advanceDto)
    {
        var command = new SenorArroz.Application.Features.DeliverymanAdvances.Commands.UpdateAdvanceCommand
        {
            Id = advanceId,
            Advance = advanceDto
        };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene todos los abonos con filtros opcionales
    /// </summary>
    [HttpGet("advances")]
    [Authorize(Roles = "Admin,Cashier,Superadmin")]
    public async Task<ActionResult<PagedResult<SenorArroz.Application.Features.DeliverymanAdvances.DTOs.DeliverymanAdvanceDto>>> GetAdvances(
        [FromQuery] int? deliverymanId = null,
        [FromQuery] int? branchId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortOrder = "desc")
    {
        var query = new SenorArroz.Application.Features.DeliverymanAdvances.Queries.GetAdvancesQuery
        {
            DeliverymanId = deliverymanId,
            BranchId = branchId,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy ?? "createdAt",
            SortOrder = sortOrder
        };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Elimina un abono
    /// </summary>
    [HttpDelete("{id:int}/advances/{advanceId:int}")]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<ActionResult> DeleteAdvance(int id, int advanceId)
    {
        var command = new SenorArroz.Application.Features.DeliverymanAdvances.Commands.DeleteAdvanceCommand
        {
            Id = advanceId
        };
        var result = await _mediator.Send(command);
        if (!result)
            return NotFound();
        return NoContent();
    }
}