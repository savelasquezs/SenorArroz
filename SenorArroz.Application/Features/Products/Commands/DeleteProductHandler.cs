// SenorArroz.Application/Features/Products/Commands/DeleteProductHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Commands;

public class DeleteProductHandler : IRequestHandler<DeleteProductCommand, bool>
{
    private readonly IProductRepository _productRepository;
    private readonly ICurrentUser _currentUser;

    public DeleteProductHandler(IProductRepository productRepository, ICurrentUser currentUser)
    {
        _productRepository = productRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        // Validate product exists
        var existingProduct = await _productRepository.GetByIdAsync(request.Id);
        if (existingProduct == null)
            return false;

        // Check if user has access to this product's branch
        if (_currentUser.Role != "superadmin" && existingProduct.Category.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar este producto");

        return await _productRepository.DeleteAsync(request.Id);
    }
}
