using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardExpenseTimeSeriesHandler
    : IRequestHandler<GetDashboardExpenseTimeSeriesQuery, DashboardExpenseTimeSeriesResponseDto>
{
    private const int MaxRangeDays = 400;
    private const int DayThreshold = 62;

    private readonly IExpenseDashboardRepository _repository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IExpenseCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardExpenseTimeSeriesHandler(
        IExpenseDashboardRepository repository,
        IExpenseRepository expenseRepository,
        IExpenseCategoryRepository categoryRepository,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _expenseRepository = expenseRepository;
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardExpenseTimeSeriesResponseDto> Handle(
        GetDashboardExpenseTimeSeriesQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(request.FromUtc, request.ToUtc);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var spanDays = (int)(to.Date - from.Date).TotalDays + 1;
        var monthly = request.Granularity?.Equals("month", StringComparison.OrdinalIgnoreCase) == true
            || (string.IsNullOrWhiteSpace(request.Granularity) && spanDays > DayThreshold);

        if (request.Granularity?.Equals("day", StringComparison.OrdinalIgnoreCase) == true)
            monthly = false;

        int? categoryId = request.CategoryId;
        int? expenseId = request.ExpenseId;

        if (expenseId.HasValue)
        {
            var exp = await _expenseRepository.GetByIdWithCategoryAsync(expenseId.Value);
            if (exp == null)
                return EmptySeries("Gasto no encontrado", from, to, monthly);

            categoryId = null;
        }

        var rows = await _repository.GetTimeSeriesAsync(
            branchFilter,
            from,
            to,
            categoryId,
            expenseId,
            monthly,
            cancellationToken);

        Dictionary<DateTime, long> dict;
        if (monthly)
        {
            dict = rows.ToDictionary(
                r => new DateTime(r.BucketStart.Year, r.BucketStart.Month, 1),
                r => r.TotalCop);
        }
        else
        {
            dict = rows.ToDictionary(r => r.BucketStart.Date, r => r.TotalCop);
        }

        var labels = new List<string>();
        var amounts = new List<long>();

        if (monthly)
        {
            for (var d = new DateTime(from.Year, from.Month, 1); d <= to.Date; d = d.AddMonths(1))
            {
                labels.Add($"{d:yyyy-MM}");
                amounts.Add(dict.GetValueOrDefault(d, 0L));
            }
        }
        else
        {
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                labels.Add(d.ToString("yyyy-MM-dd"));
                amounts.Add(dict.GetValueOrDefault(d, 0L));
            }
        }

        var seriesLabel = await ResolveSeriesLabelAsync(categoryId, expenseId, cancellationToken);

        return new DashboardExpenseTimeSeriesResponseDto
        {
            Labels = labels,
            AmountsCop = amounts,
            Granularity = monthly ? "month" : "day",
            SeriesLabel = seriesLabel,
        };
    }

    private async Task<string> ResolveSeriesLabelAsync(
        int? categoryId,
        int? expenseId,
        CancellationToken cancellationToken)
    {
        if (expenseId.HasValue)
        {
            var exp = await _expenseRepository.GetByIdWithCategoryAsync(expenseId.Value);
            if (exp != null)
                return $"Gasto · {exp.Name}";
            return "Gasto";
        }

        if (categoryId.HasValue)
        {
            var cat = await _categoryRepository.GetByIdAsync(categoryId.Value);
            if (cat != null)
                return $"Categoría · {cat.Name}";
            return "Categoría";
        }

        return "Total gastos";
    }

    private static DashboardExpenseTimeSeriesResponseDto EmptySeries(
        string label,
        DateTime from,
        DateTime to,
        bool monthly)
    {
        var labels = new List<string>();
        var amounts = new List<long>();
        if (monthly)
        {
            for (var d = new DateTime(from.Year, from.Month, 1); d <= to.Date; d = d.AddMonths(1))
            {
                labels.Add($"{d:yyyy-MM}");
                amounts.Add(0L);
            }
        }
        else
        {
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                labels.Add(d.ToString("yyyy-MM-dd"));
                amounts.Add(0L);
            }
        }

        return new DashboardExpenseTimeSeriesResponseDto
        {
            Labels = labels,
            AmountsCop = amounts,
            Granularity = monthly ? "month" : "day",
            SeriesLabel = label,
        };
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
