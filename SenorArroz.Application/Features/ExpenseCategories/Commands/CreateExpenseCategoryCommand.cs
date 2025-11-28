// SenorArroz.Application/Features/ExpenseCategories/Commands/CreateExpenseCategoryCommand.cs
using MediatR;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;

namespace SenorArroz.Application.Features.ExpenseCategories.Commands;

public class CreateExpenseCategoryCommand : IRequest<ExpenseCategoryDto>
{
    public string Name { get; set; } = string.Empty;
}


