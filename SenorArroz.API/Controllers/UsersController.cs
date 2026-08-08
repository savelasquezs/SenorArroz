// SenorArroz.API/Controllers/UsersController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Users.Commands;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Application.Features.Users.Queries;
using SenorArroz.Shared.Models;
using System.Security.Claims;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Requiere autenticación para todos los endpoints
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IBranchContext _branchContext;
    private readonly ICurrentUser _currentUser;

    public UsersController(IMediator mediator, IBranchContext branchContext, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _branchContext = branchContext;
        _currentUser = currentUser;
    }

    private int GetCurrentUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

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
        _branchContext.EnsureAccess(user.BranchId);
        return Ok(user);
    }

    /// <summary>
    /// Resumen de nómina (líneas de gasto vinculadas + delivery para domiciliarios).
    /// Admin/Superadmin: cualquier id. Domiciliario: solo su propio id.
    /// </summary>
    [HttpGet("{id:int}/payroll-insights")]
    [Authorize(Roles = "Superadmin,Admin,Deliveryman")]
    public async Task<ActionResult<UserPayrollInsightsDto>> GetPayrollInsights(
        int id,
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] string seriesGranularity = "day")
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        if (string.Equals(role, "Deliveryman", StringComparison.OrdinalIgnoreCase)
            && id != GetCurrentUserId())
            return Forbid();

        var result = await _mediator.Send(new GetUserPayrollInsightsQuery(id, from, to, seriesGranularity));
        return Ok(result);
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
        _branchContext.EnsureAccess(user.BranchId);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }

    /// <summary>
    /// Actualiza un usuario existente
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<UserDto>> UpdateUser(int id, [FromBody] UpdateUserDto updateUserDto)
    {
        var existing = await _mediator.Send(new GetUserByIdQuery(id));
        _branchContext.EnsureAccess(existing.BranchId);

        var command = new UpdateUserCommand(id, updateUserDto);
        var user = await _mediator.Send(command);
        if (!Roles.IsSuperadmin(_currentUser.Role))
            _branchContext.EnsureAccess(user.BranchId);

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
        _branchContext.EnsureAccess(user.BranchId);
        return Ok(user);
    }

    /// <summary>
    /// Elimina un usuario (soft delete)
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Superadmin")]
    public async Task<ActionResult> DeleteUser(int id)
    {
        var existing = await _mediator.Send(new GetUserByIdQuery(id));
        _branchContext.EnsureAccess(existing.BranchId);
        var command = new DeleteUserCommand(id);
        await _mediator.Send(command);
        return NoContent();
    }

    /// <summary>
    /// Actualiza email y teléfono del propio perfil
    /// </summary>
    [HttpPut("{id:int}/profile")]
    public async Task<ActionResult<UserDto>> UpdateProfile(int id, [FromBody] UpdateProfileDto dto)
    {
        if (GetCurrentUserId() != id)
            return Forbid();

        var command = new UpdateProfileCommand(id, dto);
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Sube o reemplaza la foto de perfil del usuario
    /// </summary>
    [HttpPost("{id:int}/profile-image")]
    public async Task<ActionResult<UserDto>> UploadProfileImage(int id, IFormFile file, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() != id)
            return Forbid();

        using var ms = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        await using var readStream = file.OpenReadStream();
        await readStream.CopyToAsync(ms, cancellationToken);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var command = new UploadUserProfileImageCommand(id, ms.ToArray(), ext);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }
}
