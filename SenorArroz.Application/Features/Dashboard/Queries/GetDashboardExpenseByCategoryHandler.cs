using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardExpenseByCategoryHandler
    : IRequestHandler<GetDashboardExpenseByCategoryQuery, DashboardExpenseByCategoryResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IExpenseDashboardRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardExpenseByCategoryHandler(
        IExpenseDashboardRepository repository,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<DashboardExpenseByCategoryResponseDto> Handle(
        GetDashboardExpenseByCategoryQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(request.FromUtc, request.ToUtc);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var rows = await _repository.GetTotalsByCategoryAsync(branchFilter, from, to, cancellationToken);
        var total = rows.Sum(r => r.TotalCop);

        var slices = rows.Select(r => new ExpenseCategorySliceDto
        {
            CategoryId = r.CategoryId,
            Name = string.IsNullOrWhiteSpace(r.CategoryName) ? $"#{r.CategoryId}" : r.CategoryName,
            TotalCop = r.TotalCop,
            Percent = total > 0 ? Math.Round(100.0 * r.TotalCop / total, 1) : 0,
        }).ToList();

        return new DashboardExpenseByCategoryResponseDto { Slices = slices };
    }

    private static (DateTime From, DateTime To) NormalizeRange(DateTime fromUtc, DateTime toUtc)
    {
        var from = fromUtc;
        var to = toUtc;
        if (to < from)
            (from, to) = (to, from);

        if ((to.Date - from.Date).TotalDays + 1 > MaxRangeDays)
            to = from.Date.AddDays(MaxRangeDays - 1).AddDays(1).AddTicks(-1);

        return (from, to);
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
