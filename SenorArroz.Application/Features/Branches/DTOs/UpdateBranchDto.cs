using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Branches.DTOs;

public class UpdateBranchDto
{
    [Required(ErrorMessage = "El nombre de la sucursal es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "La dirección es requerida")]
    [StringLength(200, ErrorMessage = "La dirección no puede exceder 200 caracteres")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "El teléfono principal es requerido")]
    [StringLength(10, MinimumLength = 10, ErrorMessage = "El teléfono debe tener exactamente 10 dígitos")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "El teléfono debe contener solo números")]
    public string Phone1 { get; set; } = string.Empty;

    [StringLength(10, MinimumLength = 10, ErrorMessage = "El teléfono secundario debe tener exactamente 10 dígitos")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "El teléfono secundario debe contener solo números")]
    public string? Phone2 { get; set; }
}
