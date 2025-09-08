using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.API.Middleware;

public class JwtMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public JwtMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task Invoke(HttpContext context, IAuthRepository authRepository)
    {
        var token = context.Request.Headers.Authorization
            .FirstOrDefault()?.Split(" ").Last();

        if (token != null)
            await AttachUserToContext(context, authRepository, token);

        await _next(context);
    }

    private async Task AttachUserToContext(HttpContext context, IAuthRepository authRepository, string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration["JwtSettings:SecretKey"]!);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _configuration["JwtSettings:Issuer"],
                ValidateAudience = true,
                ValidAudience = _configuration["JwtSettings:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is JwtSecurityToken jwtToken &&
                jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
                {
                    // Verificar que el usuario sigue activo en la base de datos
                    var user = await authRepository.GetUserByIdWithBranchAsync(userId);
                    if (user != null && user.Active)
                    {
                        // Attach user to context on successful jwt validation
                        context.Items["User"] = user;
                        context.Items["UserId"] = userId;
                    }
                }
            }
        }
        catch
        {
            // Token validation failed, do nothing
            // User will not be attached to context
        }
    }
}