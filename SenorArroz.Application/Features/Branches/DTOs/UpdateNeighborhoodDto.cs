using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Branches.DTOs;

public class UpdateNeighborhoodDto
{
    [Required(ErrorMessage = "El nombre del barrio es requerido")]
    [StringLength(150, ErrorMessage = "El nombre no puede exceder 150 caracteres")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "La tarifa de domicilio es requerida")]
    [Range(0, 50000, ErrorMessage = "La tarifa debe estar entre 0 y 50,000")]
    public int DeliveryFee { get; set; }
}