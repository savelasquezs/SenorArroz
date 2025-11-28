// SenorArroz.Application/Features/ExpenseCategories/Queries/GetExpenseCategoriesHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.ExpenseCategories.Queries;

public class GetExpenseCategoriesHandler : IRequestHandler<GetExpenseCategoriesQuery, PagedResult<ExpenseCategoryDto>>
{
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public GetExpenseCategoriesHandler(
        IExpenseCategoryRepository categoryRepository,
        IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<PagedResult<ExpenseCategoryDto>> Handle(GetExpenseCategoriesQuery request, CancellationToken cancellationToken)
    {
        var pagedCategories = await _categoryRepository.GetPagedAsync(
            request.Name,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var categoryDtos = new List<ExpenseCategoryDto>();

        foreach (var category in pagedCategories.Items)
        {
            var categoryDto = _mapper.Map<ExpenseCategoryDto>(category);

            // Add statistics
            categoryDto.TotalExpenses = await _categoryRepository.GetTotalExpensesAsync(category.Id);

            categoryDtos.Add(categoryDto);
        }

        return new PagedResult<ExpenseCategoryDto>
        {
            Items = categoryDtos,
            TotalCount = pagedCategories.TotalCount,
            Page = pagedCategories.Page,
            PageSize = pagedCategories.PageSize,
            TotalPages = pagedCategories.TotalPages
        };
    }
}


