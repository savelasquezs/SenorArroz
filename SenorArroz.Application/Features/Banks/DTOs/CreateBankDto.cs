// SenorArroz.Application/Features/Banks/DTOs/CreateBankDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Banks.DTOs;

public class CreateBankDto
{
    [Required(ErrorMessage = "La sucursal es requerida")]
    public int BranchId { get; set; }

    [Required(ErrorMessage = "El nombre del banco es requerido")]
    [StringLength(150, ErrorMessage = "El nombre no puede exceder 150 caracteres")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "La URL de la imagen no puede exceder 200 caracteres")]
    public string? ImageUrl { get; set; }

    public bool Active { get; set; } = true;
}
