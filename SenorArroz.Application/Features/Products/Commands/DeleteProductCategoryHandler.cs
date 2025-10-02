// SenorArroz.Application/Features/Products/Commands/DeleteProductCategoryHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Commands;

public class DeleteProductCategoryHandler : IRequestHandler<DeleteProductCategoryCommand, bool>
{
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;

    public DeleteProductCategoryHandler(
        IProductCategoryRepository categoryRepository,
        ICurrentUser currentUser)
    {
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeleteProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.Id);
        if (category == null)
        {
            throw new NotFoundException($"Categoría con ID {request.Id} no encontrada");
        }

        // Validate access: admin can only delete categories from their branch
        if (_currentUser.Role != "superadmin" && category.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No tienes permisos para eliminar esta categoría");
        }

        // Check if category has products
        var categoryWithProducts = await _categoryRepository.GetByIdWithProductsAsync(request.Id);
        if (categoryWithProducts != null && categoryWithProducts.Products.Any())
        {
            throw new BusinessException("No se puede eliminar una categoría que tiene productos asociados");
        }

        return await _categoryRepository.DeleteAsync(request.Id);
    }
}