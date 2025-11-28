// SenorArroz.Application/Features/Expenses/Queries/GetAllExpensesHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetAllExpensesHandler : IRequestHandler<GetAllExpensesQuery, IEnumerable<ExpenseDto>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IMapper _mapper;

    public GetAllExpensesHandler(
        IExpenseRepository expenseRepository,
        IMapper mapper)
    {
        _expenseRepository = expenseRepository;
        _mapper = mapper;
    }

    public async Task<IEnumerable<ExpenseDto>> Handle(GetAllExpensesQuery request, CancellationToken cancellationToken)
    {
        var expenses = await _expenseRepository.GetAllAsync();
        return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
    }
}

