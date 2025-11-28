// SenorArroz.Application/Features/Expenses/DTOs/UpdateExpenseDto.cs
using System.ComponentModel.DataAnnotations;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Expenses.DTOs;

public class UpdateExpenseDto
{
    [Required(ErrorMessage = "El nombre del gasto es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "La categoría es requerida")]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "La unidad es requerida")]
    public ExpenseUnit Unit { get; set; } = ExpenseUnit.Unit;
}


