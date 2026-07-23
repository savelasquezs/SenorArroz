using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Auth.DTOs
{
    public class LoginDto
    {
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "El formato del email no es válido")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es requerida")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
        public string Password { get; set; } = string.Empty;

        [MaxLength(64, ErrorMessage = "El identificador del dispositivo no puede superar 64 caracteres")]
        public string? DeviceInstallationId { get; set; }
    }
}
