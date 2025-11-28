// SenorArroz.Application/Features/ExpenseCategories/Queries/GetExpenseCategoryByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseCategories.Queries;

public class GetExpenseCategoryByIdHandler : IRequestHandler<GetExpenseCategoryByIdQuery, ExpenseCategoryDto?>
{
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public GetExpenseCategoryByIdHandler(
        IExpenseCategoryRepository categoryRepository,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<ExpenseCategoryDto?> Handle(GetExpenseCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(request.Id);
        if (category == null)
            return null;

        var categoryDto = _mapper.Map<ExpenseCategoryDto>(category);

        // Add statistics
        categoryDto.TotalExpenses = await _categoryRepository.GetTotalExpensesAsync(category.Id);

        return categoryDto;
    }
}

