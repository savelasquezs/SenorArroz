using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SenorArroz.Infrastructure.Services;

public class JwtService : IJwtService
{
    private readonly IConfiguration _configuration;
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenExpirationMinutes;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;
        _secretKey = _configuration["JwtSettings:SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
        _issuer = _configuration["JwtSettings:Issuer"] ?? throw new InvalidOperationException("JWT Issuer not configured");
        _audience = _configuration["JwtSettings:Audience"] ?? throw new InvalidOperationException("JWT Audience not configured");
        _accessTokenExpirationMinutes = int.Parse(_configuration["JwtSettings:AccessTokenExpirationMinutes"] ?? "60");
    }

    public string GenerateAccessToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_secretKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role?.ToString() ?? string.Empty),
            new("branch_id", user.BranchId.ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_accessTokenExpirationMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public int? GetUserIdFromExpiredToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_secretKey);

        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = false, // Important: Don't validate lifetime for expired tokens
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
            return userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId) ? userId : null;
        }
        catch
        {
            return null;
        }
    }

    public bool IsTokenExpired(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        try
        {
            var jsonToken = tokenHandler.ReadJwtToken(token);
            return jsonToken.ValidTo < DateTime.UtcNow;
        }
        catch
        {
            return true;
        }
    }
}