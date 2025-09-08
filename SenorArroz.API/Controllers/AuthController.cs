using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Auth.Commands;
using SenorArroz.Application.Features.Auth.DTOs;

using System.Security.Claims;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public AuthController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    /// <summary>
    /// Iniciar sesión
    /// </summary>
    /// <param name="loginDto">Credenciales de login</param>
    /// <returns>Token de acceso y refresh token</returns>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto loginDto)
    {
        var command = _mapper.Map<LoginCommand>(loginDto);
        command.IpAddress = GetIpAddress();

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Renovar token de acceso
    /// </summary>
    /// <param name="refreshTokenDto">Refresh token</param>
    /// <returns>Nuevo token de acceso y refresh token</returns>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponseDto>> RefreshToken([FromBody] RefreshTokenDto refreshTokenDto)
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (authHeader == null || !authHeader.StartsWith("Bearer "))
        {
            return BadRequest("Token de acceso requerido");
        }

        var token = authHeader["Bearer ".Length..].Trim();

        var command = new RefreshTokenCommand
        {
            Token = token,
            RefreshToken = refreshTokenDto.RefreshToken,
            IpAddress = GetIpAddress()
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Cerrar sesión
    /// </summary>
    /// <param name="refreshTokenDto">Refresh token a revocar</param>
    /// <returns>Confirmación de logout</returns>
    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult> Logout([FromBody] RefreshTokenDto refreshTokenDto)
    {
        var command = new LogoutCommand
        {
            RefreshToken = refreshTokenDto.RefreshToken,
            IpAddress = GetIpAddress()
        };

        var result = await _mediator.Send(command);

        if (result)
            return Ok(new { message = "Sesión cerrada exitosamente" });

        return BadRequest("Error al cerrar sesión");
    }

    /// <summary>
    /// Cambiar contraseña
    /// </summary>
    /// <param name="changePasswordDto">Datos para cambio de contraseña</param>
    /// <returns>Confirmación del cambio</returns>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordDto changePasswordDto)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized();

        var command = _mapper.Map<ChangePasswordCommand>(changePasswordDto);
        command.UserId = userId.Value;

        var result = await _mediator.Send(command);

        if (result)
            return Ok(new { message = "Contraseña cambiada exitosamente" });

        return BadRequest("Error al cambiar la contraseña. Verifica tu contraseña actual.");
    }

    /// <summary>
    /// Obtener información del usuario autenticado
    /// </summary>
    /// <returns>Información del usuario</returns>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<object> GetCurrentUser()
    {
        var userId = GetCurrentUserId();
        var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        var userName = User.FindFirst(ClaimTypes.Name)?.Value;
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var branchId = User.FindFirst("branch_id")?.Value;

        return Ok(new
        {
            id = userId,
            email = userEmail,
            name = userName,
            role = userRole,
            branchId = branchId != null ? int.Parse(branchId) : 0
        });
    }

    /// <summary>
    /// Validar si el token actual es válido
    /// </summary>
    /// <returns>Estado del token</returns>
    [HttpGet("validate")]
    [Authorize]
    public ActionResult ValidateToken()
    {
        return Ok(new
        {
            valid = true,
            message = "Token válido",
            expiresAt = User.FindFirst("exp")?.Value
        });
    }

    #region Private Methods

    private string GetIpAddress()
    {
        // Intentar obtener la IP real del cliente
        var ipAddress = Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (string.IsNullOrEmpty(ipAddress))
            ipAddress = Request.Headers["X-Real-IP"].FirstOrDefault();

        if (string.IsNullOrEmpty(ipAddress))
            ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        return ipAddress ?? "unknown";
    }

    private int? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        return userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId) ? userId : null;
    }

    #endregion
}