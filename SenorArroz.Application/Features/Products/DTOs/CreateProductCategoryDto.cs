// SenorArroz.Application/Features/Products/DTOs/CreateProductCategoryDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Products.DTOs;

public class CreateProductCategoryDto
{
    [Required(ErrorMessage = "El nombre de la categoría es requerido")]
    [StringLength(150, ErrorMessage = "El nombre no puede exceder 150 caracteres")]
    public string Name { get; set; } = string.Empty;

    // BranchId opcional, solo para superadmin. Admin usa su branch automáticamente
    public int? BranchId { get; set; }
}