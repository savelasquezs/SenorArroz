// SenorArroz.Application/Features/Products/Commands/DeleteProductCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.Products.Commands;

public class DeleteProductCommand : IRequest<bool>
{
    public int Id { get; set; }
}
