using MediatR;
using SenorArroz.Application.Common.Helpers;
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
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var d0 = ColombiaTimeHelper.ConvertUtcToColombiaCalendarDate(from);
        var d1 = ColombiaTimeHelper.ConvertUtcToColombiaCalendarDate(to);
        var spanDays = (int)(d1 - d0).TotalDays + 1;
        var monthly = request.Granularity?.Equals("month", StringComparison.OrdinalIgnoreCase) == true
            || (string.IsNullOrWhiteSpace(request.Granularity) && spanDays > DayThreshold);

        if (request.Granularity?.Equals("day", StringComparison.OrdinalIgnoreCase) == true)
            monthly = false;

        int? categoryId = request.CategoryId;
        int? expenseId = request.ExpenseId;

        if (expenseId.HasValue)
        {
            var exp = await _expenseRepository.GetByIdWithCategoryAsync(expenseId.Value, cancellationToken);
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
                r => new DateTime(r.BucketStart.Year, r.BucketStart.Month, 1, 0, 0, 0, DateTimeKind.Unspecified),
                r => r.TotalCop);
        }
        else
        {
            dict = rows.ToDictionary(
                r => DateTime.SpecifyKind(r.BucketStart.Date, DateTimeKind.Unspecified),
                r => r.TotalCop);
        }

        var labels = new List<string>();
        var amounts = new List<long>();

        if (monthly)
        {
            for (var d = new DateTime(d0.Year, d0.Month, 1);
                 d <= new DateTime(d1.Year, d1.Month, 1);
                 d = d.AddMonths(1))
            {
                var key = new DateTime(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
                labels.Add($"{key:yyyy-MM}");
                amounts.Add(dict.GetValueOrDefault(key, 0L));
            }
        }
        else
        {
            for (var d = d0; d <= d1; d = d.AddDays(1))
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
            var exp = await _expenseRepository.GetByIdWithCategoryAsync(expenseId.Value, cancellationToken);
            if (exp != null)
                return $"Gasto · {exp.Name}";
            return "Gasto";
        }

        if (categoryId.HasValue)
        {
            var cat = await _categoryRepository.GetByIdAsync(categoryId.Value, cancellationToken);
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
        var d0 = ColombiaTimeHelper.ConvertUtcToColombiaCalendarDate(from);
        var d1 = ColombiaTimeHelper.ConvertUtcToColombiaCalendarDate(to);

        var labels = new List<string>();
        var amounts = new List<long>();
        if (monthly)
        {
            for (var d = new DateTime(d0.Year, d0.Month, 1);
                 d <= new DateTime(d1.Year, d1.Month, 1);
                 d = d.AddMonths(1))
            {
                labels.Add($"{d:yyyy-MM}");
                amounts.Add(0L);
            }
        }
        else
        {
            for (var d = d0; d <= d1; d = d.AddDays(1))
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

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
