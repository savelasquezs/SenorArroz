// SenorArroz.Application/Features/Expenses/Queries/GetExpenseByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Application.Features.Expenses.Helpers;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetExpenseByIdHandler : IRequestHandler<GetExpenseByIdQuery, ExpenseDto?>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetExpenseByIdHandler(
        IExpenseRepository expenseRepository,
        IApplicationDbContext context,
        IMapper mapper)
    {
        _expenseRepository = expenseRepository;
        _context = context;
        _mapper = mapper;
    }

    public async Task<ExpenseDto?> Handle(GetExpenseByIdQuery request, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.GetByIdAsync(request.Id, cancellationToken);
        if (expense == null)
            return null;

        var dto = _mapper.Map<ExpenseDto>(expense);
        await ExpenseMenuTargetCommandsHelper.EnrichMenuTargetsAsync(dto, _context, cancellationToken);
        return dto;
    }
}
