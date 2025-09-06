// SenorArroz.API/Controllers/UsersController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Users.Commands;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Application.Features.Users.Queries;

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
    /// Obtiene todos los usuarios, opcionalmente filtrados por sucursal
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers([FromQuery] int? branchId = null)
    {
        var query = new GetUsersQuery(branchId);
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
}