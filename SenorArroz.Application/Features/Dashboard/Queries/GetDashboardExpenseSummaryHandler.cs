using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardExpenseSummaryHandler
    : IRequestHandler<GetDashboardExpenseSummaryQuery, DashboardExpenseSummaryResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IExpenseDashboardRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardExpenseSummaryHandler(
        IExpenseDashboardRepository repository,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<DashboardExpenseSummaryResponseDto> Handle(
        GetDashboardExpenseSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var current = await _repository.GetPeriodTotalsAsync(branchFilter, from, to, cancellationToken);

        var d0 = ColombiaTimeHelper.ConvertUtcToColombiaCalendarDate(from);
        var d1 = ColombiaTimeHelper.ConvertUtcToColombiaCalendarDate(to);
        var inclusiveDays = Math.Max(1, (int)(d1 - d0).TotalDays + 1);
        var prevD1 = d0.AddDays(-1);
        var prevD0 = prevD1.AddDays(-(inclusiveDays - 1));
        var (prevFrom, prevTo) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(prevD0, prevD1);
        var previous = await _repository.GetPeriodTotalsAsync(branchFilter, prevFrom, prevTo, cancellationToken);
        var avgDaily = current.TotalCop / (double)inclusiveDays;
        var avgTicket = current.HeaderCount > 0 ? current.TotalCop / (double)current.HeaderCount : 0d;

        return new DashboardExpenseSummaryResponseDto
        {
            TotalCop = current.TotalCop,
            HeaderCount = current.HeaderCount,
            LineCount = current.LineCount,
            AvgDailyCop = Math.Round(avgDaily, 2),
            AvgTicketCop = Math.Round(avgTicket, 2),
            PreviousPeriodTotalCop = previous.TotalCop,
            PreviousPeriodHeaderCount = previous.HeaderCount,
            TotalChangeFromPreviousPercent = PercentChangeLong(current.TotalCop, previous.TotalCop),
        };
    }

    private static double PercentChangeLong(long current, long previous)
    {
        if (previous == 0)
            return current > 0 ? 100d : 0d;
        return Math.Round((current - (double)previous) / previous * 100d, 2);
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
