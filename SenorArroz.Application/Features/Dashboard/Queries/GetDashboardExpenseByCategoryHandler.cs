using MediatR;
using SenorArroz.Application.Common.Helpers;
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
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);
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

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
