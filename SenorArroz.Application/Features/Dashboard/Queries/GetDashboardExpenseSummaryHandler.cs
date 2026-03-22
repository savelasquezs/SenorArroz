using MediatR;
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
        var (from, to) = NormalizeRange(request.FromUtc, request.ToUtc);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var current = await _repository.GetPeriodTotalsAsync(branchFilter, from, to, cancellationToken);

        var duration = to - from;
        var prevTo = from.AddTicks(-1);
        var prevFrom = prevTo - duration;
        var previous = await _repository.GetPeriodTotalsAsync(branchFilter, prevFrom, prevTo, cancellationToken);

        var inclusiveDays = Math.Max(1, (int)(to.Date - from.Date).TotalDays + 1);
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
