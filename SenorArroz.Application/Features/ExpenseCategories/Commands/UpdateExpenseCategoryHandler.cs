// SenorArroz.Application/Features/ExpenseCategories/Commands/UpdateExpenseCategoryHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseCategories.Commands;

public class UpdateExpenseCategoryHandler : IRequestHandler<UpdateExpenseCategoryCommand, ExpenseCategoryDto>
{
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public UpdateExpenseCategoryHandler(
        IExpenseCategoryRepository categoryRepository,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<ExpenseCategoryDto> Handle(UpdateExpenseCategoryCommand request, CancellationToken cancellationToken)
    {
        // Validate permissions
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
        {
            throw new BusinessException("No tienes permisos para modificar categorías de gastos");
        }

        var category = await _categoryRepository.GetByIdAsync(request.Id);
        if (category == null)
        {
            throw new NotFoundException($"Categoría con ID {request.Id} no encontrada");
        }

        // Validate name doesn't exist for other categories
        if (await _categoryRepository.NameExistsAsync(request.Name, request.Id))
        {
            throw new BusinessException($"Ya existe otra categoría con el nombre '{request.Name}'");
        }

        // Update category
        category.Name = request.Name.Trim();

        category = await _categoryRepository.UpdateAsync(category);

        var categoryDto = _mapper.Map<ExpenseCategoryDto>(category);

        // Add current statistics
        categoryDto.TotalExpenses = await _categoryRepository.GetTotalExpensesAsync(category.Id);

        return categoryDto;
    }
}

