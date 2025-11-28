// SenorArroz.Application/Features/ExpenseCategories/Queries/GetAllExpenseCategoriesHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseCategories.Queries;

public class GetAllExpenseCategoriesHandler : IRequestHandler<GetAllExpenseCategoriesQuery, IEnumerable<ExpenseCategoryDto>>
{
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public GetAllExpenseCategoriesHandler(
        IExpenseCategoryRepository categoryRepository,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<IEnumerable<ExpenseCategoryDto>> Handle(GetAllExpenseCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetAllAsync();
        return _mapper.Map<IEnumerable<ExpenseCategoryDto>>(categories);
    }
}

