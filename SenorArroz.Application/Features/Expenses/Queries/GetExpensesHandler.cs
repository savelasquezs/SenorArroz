// SenorArroz.Application/Features/Expenses/Queries/GetExpensesHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetExpensesHandler : IRequestHandler<GetExpensesQuery, PagedResult<ExpenseDto>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IMapper _mapper;

    public GetExpensesHandler(
        IExpenseRepository expenseRepository,
        IMapper mapper)
    {
        _expenseRepository = expenseRepository;
        _mapper = mapper;
    }

    public async Task<PagedResult<ExpenseDto>> Handle(GetExpensesQuery request, CancellationToken cancellationToken)
    {
        var pagedExpenses = await _expenseRepository.GetPagedAsync(
            request.CategoryId,
            request.Name,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var expenseDtos = _mapper.Map<List<ExpenseDto>>(pagedExpenses.Items);

        return new PagedResult<ExpenseDto>
        {
            Items = expenseDtos,
            TotalCount = pagedExpenses.TotalCount,
            Page = pagedExpenses.Page,
            PageSize = pagedExpenses.PageSize,
            TotalPages = pagedExpenses.TotalPages
        };
    }
}

