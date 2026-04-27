using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardExpenseTopLinesHandler
    : IRequestHandler<GetDashboardExpenseTopLinesQuery, DashboardExpenseTopLinesResponseDto>
{
    private const int MaxRangeDays = 400;
    public const int DefaultLimit = 15;
    public const int MinLimit = 1;
    public const int MaxLimit = 500;

    private readonly IExpenseDashboardRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardExpenseTopLinesHandler(
        IExpenseDashboardRepository repository,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<DashboardExpenseTopLinesResponseDto> Handle(
        GetDashboardExpenseTopLinesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.CategoryId <= 0)
            throw new BusinessException("Debe indicar categoryId (categoría de línea) mayor que cero.");

        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);
        var branchFilter = ResolveBranchFilter(request.BranchId);
        var take = ResolveLimit(request.Limit);

        var rows = await _repository.GetTopExpenseCatalogAggregatesAsync(
            branchFilter,
            from,
            to,
            request.CategoryId,
            request.ExpenseId,
            take,
            cancellationToken);

        var items = rows.Select(r => new ExpenseCatalogAggregateItemDto
        {
            ExpenseId = r.ExpenseId,
            ExpenseName = r.ExpenseName,
            CategoryName = r.CategoryName,
            TotalCop = r.TotalCop,
            LineCount = r.LineCount,
        }).ToList();

        return new DashboardExpenseTopLinesResponseDto
        {
            Items = items,
            LimitApplied = take,
        };
    }

    private int ResolveLimit(int? limit)
    {
        if (!limit.HasValue)
            return DefaultLimit;
        if (limit.Value < MinLimit)
            return MinLimit;
        if (limit.Value > MaxLimit)
            return MaxLimit;
        return limit.Value;
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
