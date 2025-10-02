// SenorArroz.Application/Features/Products/DTOs/CreateProductDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Products.DTOs;

public class CreateProductDto
{
    [Required(ErrorMessage = "La categoría es requerida")]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "El nombre del producto es requerido")]
    [StringLength(150, ErrorMessage = "El nombre no puede exceder 150 caracteres")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "El precio es requerido")]
    [Range(0, int.MaxValue, ErrorMessage = "El precio debe ser mayor o igual a 0")]
    public int Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "El stock debe ser mayor o igual a 0")]
    public int? Stock { get; set; }

    public bool Active { get; set; } = true;
}