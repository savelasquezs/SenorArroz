using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Application.Features.Expenses.Services;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetExpenseMenuAttributionHandler
    : IRequestHandler<GetExpenseMenuAttributionQuery, ExpenseMenuAttributionResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IApplicationDbContext _context;
    private readonly IExpenseDashboardRepository _expenseDashboardRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetExpenseMenuAttributionHandler(
        IApplicationDbContext context,
        IExpenseDashboardRepository expenseDashboardRepository,
        IOrderRepository orderRepository,
        ICurrentUser currentUser)
    {
        _context = context;
        _expenseDashboardRepository = expenseDashboardRepository;
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<ExpenseMenuAttributionResponseDto> Handle(
        GetExpenseMenuAttributionQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
            throw new BusinessException("No tienes permisos para ver la imputación de gastos a menú");

        var (from, to) = NormalizeRange(request.FromUtc, request.ToUtc);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var allTargets = await _context.ExpenseMenuTargets
            .AsNoTracking()
            .Include(t => t.Expense)
            .ToListAsync(cancellationToken);

        if (allTargets.Count == 0)
        {
            return new ExpenseMenuAttributionResponseDto
            {
                FromUtc = from,
                ToUtc = to,
                BranchId = branchFilter,
                Lines = new List<ExpenseMenuAttributionLineDto>(),
            };
        }

        var targetsGrouped = allTargets.GroupBy(t => t.ExpenseId).ToList();
        var expenseIds = targetsGrouped.Select(g => g.Key).ToList();

        var expenseTotals = await _expenseDashboardRepository.GetTotalsByExpenseCatalogIdsInRangeAsync(
            branchFilter,
            from,
            to,
            expenseIds,
            cancellationToken);

        var catWeights = await _orderRepository.GetSalesCategoryWeightAggregatesForDashboardAsync(
            branchFilter,
            from,
            to,
            cancellationToken);
        var prodWeights = await _orderRepository.GetSalesProductWeightAggregatesForDashboardAsync(
            branchFilter,
            from,
            to,
            cancellationToken);

        var gramsByCategory = catWeights.ToDictionary(r => r.CategoryId, r => r.TotalWeightGrams);
        var gramsByProduct = prodWeights.ToDictionary(r => r.ProductId, r => r.TotalWeightGrams);

        var allCatIds = allTargets
            .Where(t => t.TargetType == ExpenseMenuTargetType.ProductCategory)
            .Select(t => t.TargetId)
            .Distinct()
            .ToList();
        var allProdIds = allTargets
            .Where(t => t.TargetType == ExpenseMenuTargetType.Product)
            .Select(t => t.TargetId)
            .Distinct()
            .ToList();

        var catNames = allCatIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.ProductCategories
                .AsNoTracking()
                .Where(c => allCatIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name ?? $"#{c.Id}", cancellationToken);

        var prodNames = allProdIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Products
                .AsNoTracking()
                .Where(p => allProdIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name ?? $"#{p.Id}", cancellationToken);

        var nameByKey = new Dictionary<AttributionTargetKey, string>();
        foreach (var cid in allCatIds)
            nameByKey[new AttributionTargetKey(ExpenseMenuTargetType.ProductCategory, cid)] = catNames.GetValueOrDefault(cid, $"#{cid}");
        foreach (var pid in allProdIds)
            nameByKey[new AttributionTargetKey(ExpenseMenuTargetType.Product, pid)] = prodNames.GetValueOrDefault(pid, $"#{pid}");

        var lines = new List<ExpenseMenuAttributionLineDto>();
        foreach (var group in targetsGrouped)
        {
            var expenseName = group.First().Expense?.Name ?? $"#{group.Key}";
            var totalCop = expenseTotals.GetValueOrDefault(group.Key, 0L);
            var targetTuples = group
                .Select(t => (t.TargetType, t.TargetId))
                .ToList();

            var built = ExpenseMenuAttributionCalculator.BuildLines(
                group.Key,
                expenseName,
                totalCop,
                targetTuples,
                gramsByCategory,
                gramsByProduct,
                nameByKey);

            foreach (var row in built)
            {
                lines.Add(new ExpenseMenuAttributionLineDto
                {
                    ExpenseId = row.ExpenseId,
                    ExpenseName = row.ExpenseName,
                    TotalExpenseInPeriodCop = row.TotalExpenseInPeriodCop,
                    TargetType = row.TargetType,
                    TargetId = row.TargetId,
                    TargetName = row.TargetName,
                    AllocatedCop = row.AllocatedCop,
                    TotalWeightGramsSold = row.TotalWeightGramsSold,
                    CostPerGramCop = row.CostPerGramCop,
                });
            }
        }

        lines.Sort((a, b) =>
        {
            var c = string.Compare(a.ExpenseName, b.ExpenseName, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = a.TargetType.CompareTo(b.TargetType);
            if (c != 0) return c;
            return a.TargetId.CompareTo(b.TargetId);
        });

        return new ExpenseMenuAttributionResponseDto
        {
            FromUtc = from,
            ToUtc = to,
            BranchId = branchFilter,
            Lines = lines,
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
