// SenorArroz.Application/Features/ExpenseCategories/Commands/UpdateExpenseCategoryCommand.cs
using MediatR;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;

namespace SenorArroz.Application.Features.ExpenseCategories.Commands;

public class UpdateExpenseCategoryCommand : IRequest<ExpenseCategoryDto>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}


