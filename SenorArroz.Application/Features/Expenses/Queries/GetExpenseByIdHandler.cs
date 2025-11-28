// SenorArroz.Application/Features/Expenses/Queries/GetExpenseByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetExpenseByIdHandler : IRequestHandler<GetExpenseByIdQuery, ExpenseDto?>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IMapper _mapper;

    public GetExpenseByIdHandler(
        IExpenseRepository expenseRepository,
        IMapper mapper)
    {
        _expenseRepository = expenseRepository;
        _mapper = mapper;
    }

    public async Task<ExpenseDto?> Handle(GetExpenseByIdQuery request, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.GetByIdAsync(request.Id);
        if (expense == null)
            return null;

        return _mapper.Map<ExpenseDto>(expense);
    }
}

