// SenorArroz.Application/Features/Products/Commands/DeleteProductHandler.cs
using MediatR;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Commands;

public class DeleteProductHandler : IRequestHandler<DeleteProductCommand, bool>
{
    private readonly IProductRepository _productRepository;

    public DeleteProductHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<bool> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        // Validate product exists
        var existingProduct = await _productRepository.GetByIdAsync(request.Id);
        if (existingProduct == null)
            return false;

        return await _productRepository.DeleteAsync(request.Id);
    }
}
