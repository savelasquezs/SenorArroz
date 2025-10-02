// SenorArroz.Application/Features/Products/DTOs/UpdateProductCategoryDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Products.DTOs;

public class UpdateProductCategoryDto
{
    [Required(ErrorMessage = "El nombre de la categoría es requerido")]
    [StringLength(150, ErrorMessage = "El nombre no puede exceder 150 caracteres")]
    public string Name { get; set; } = string.Empty;
}