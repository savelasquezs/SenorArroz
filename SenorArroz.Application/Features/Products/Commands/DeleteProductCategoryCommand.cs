// SenorArroz.Application/Features/Products/Commands/DeleteProductCategoryCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.Products.Commands;

public class DeleteProductCategoryCommand : IRequest<bool>
{
    public int Id { get; set; }
}