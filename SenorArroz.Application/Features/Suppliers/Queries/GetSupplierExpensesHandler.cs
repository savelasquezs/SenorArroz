using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSupplierExpensesHandler : IRequestHandler<GetSupplierExpensesQuery, List<SupplierExpenseDto>>
{
    private readonly IApplicationDbContext _context;

    public GetSupplierExpensesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<SupplierExpenseDto>> Handle(GetSupplierExpensesQuery request, CancellationToken cancellationToken)
    {
        var favorites = await _context.SupplierExpenses
            .AsNoTracking()
            .Where(se => se.SupplierId == request.SupplierId)
            .OrderByDescending(se => se.UsageCount)
            .ThenByDescending(se => se.LastUsedAt)
            .Take(50)
            .Select(se => new SupplierExpenseDto
            {
                ExpenseId = se.ExpenseId,
                ExpenseName = se.Expense.Name,
                ExpenseUnit = se.Expense.Unit.ToString(),
                UsageCount = se.UsageCount,
                LastUsedAt = se.LastUsedAt,
                LastUnitPrice = se.LastUnitPrice
            })
            .ToListAsync(cancellationToken);

        return favorites;
    }
}


