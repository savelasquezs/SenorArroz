// SenorArroz.Application/Features/ExpenseCategories/DTOs/CreateExpenseCategoryDto.cs
using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.ExpenseCategories.DTOs;

public class CreateExpenseCategoryDto
{
    [Required(ErrorMessage = "El nombre de la categoría es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    public string Name { get; set; } = string.Empty;
}

