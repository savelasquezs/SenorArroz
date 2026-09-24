// SenorArroz.Application/Features/Expenses/Commands/UpdateExpenseCommand.cs
using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Application.Features.Inventory.DTOs;

namespace SenorArroz.Application.Features.Expenses.Commands;

public class UpdateExpenseCommand : IRequest<ExpenseDto>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public Domain.Enums.ExpenseUnit Unit { get; set; }
    public List<ExpenseMenuTargetInputDto> MenuTargets { get; set; } = new();
    public bool TracksInventory { get; set; }
    public bool InventoryActive { get; set; }
    public Domain.Enums.InventoryBaseUnit InventoryBaseUnit { get; set; }
    public List<InventoryConversionInput> InventoryConversions { get; set; } = new();
}


