// SenorArroz.Application/Features/ExpenseCategories/Commands/DeleteExpenseCategoryCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.ExpenseCategories.Commands;

public class DeleteExpenseCategoryCommand : IRequest<bool>
{
    public int Id { get; set; }
}

