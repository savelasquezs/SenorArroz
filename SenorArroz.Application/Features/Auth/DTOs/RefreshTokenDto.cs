using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Auth.DTOs;

public class RefreshTokenDto
{
    [Required(ErrorMessage = "El refresh token es requerido")]
    public string RefreshToken { get; set; } = string.Empty;
}