using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Auth.DTOs
{
    public class ValidateResetTokenDto
    {
        [Required(ErrorMessage = "El token es requerido")]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "El formato del email no es válido")]
        public string Email { get; set; } = string.Empty;
    }
}