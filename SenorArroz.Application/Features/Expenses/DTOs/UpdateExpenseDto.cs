// SenorArroz.Application/Features/Expenses/DTOs/UpdateExpenseDto.cs
using System.ComponentModel.DataAnnotations;
using SenorArroz.Domain.Enums;
using SenorArroz.Application.Features.Inventory.DTOs;

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

    public List<ExpenseMenuTargetInputDto> MenuTargets { get; set; } = new();
    public bool TracksInventory { get; set; }
    public bool InventoryActive { get; set; }
    public InventoryBaseUnit InventoryBaseUnit { get; set; } = InventoryBaseUnit.Unit;
    public List<InventoryConversionInput> InventoryConversions { get; set; } = new();
}


