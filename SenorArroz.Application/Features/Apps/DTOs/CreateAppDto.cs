// SenorArroz.Application/Features/Apps/DTOs/CreateAppDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Apps.DTOs;

public class CreateAppDto
{
    [Required(ErrorMessage = "El banco es requerido")]
    public int BankId { get; set; }

    [Required(ErrorMessage = "El nombre de la app es requerido")]
    [StringLength(150, ErrorMessage = "El nombre no puede exceder 150 caracteres")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "La URL de la imagen no puede exceder 200 caracteres")]
    public string? ImageUrl { get; set; }

    public bool Active { get; set; } = true;
}
