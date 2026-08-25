// SenorArroz.Application/Features/Products/DTOs/UpdateProductDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Products.DTOs;

public class UpdateProductDto
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

    [Range(0, int.MaxValue, ErrorMessage = "El peso en gramos debe ser mayor o igual a 0")]
    public int? WeightGrams { get; set; }

    public bool Active { get; set; } = true;
    public int? CommercialProfileId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "El mínimo de personas debe ser mayor que cero")]
    public int? ServesPeopleMin { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "El máximo de personas debe ser mayor que cero")]
    public int? ServesPeopleMax { get; set; }
    [StringLength(80)]
    public string? StorefrontVariantLabel { get; set; }
    [Range(0, int.MaxValue)]
    public int StorefrontSortOrder { get; set; }
}
