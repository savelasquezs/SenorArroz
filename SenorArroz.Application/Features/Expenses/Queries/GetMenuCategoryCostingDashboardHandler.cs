using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Application.Features.Expenses.Services;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetMenuCategoryCostingDashboardHandler
    : IRequestHandler<GetMenuCategoryCostingDashboardQuery, MenuCategoryCostingDashboardResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IApplicationDbContext _context;
    private readonly IExpenseDashboardRepository _expenseDashboardRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetMenuCategoryCostingDashboardHandler(
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

    public async Task<MenuCategoryCostingDashboardResponseDto> Handle(
        GetMenuCategoryCostingDashboardQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.Role != "admin" && _currentUser.Role != "superadmin")
            throw new BusinessException("No tienes permisos para ver el costeo por categoría de menú");

        var (from, to) = NormalizeRange(request.FromUtc, request.ToUtc);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var catWeightRows = await _orderRepository.GetSalesCategoryWeightAggregatesForDashboardAsync(
            branchFilter,
            from,
            to,
            cancellationToken);
        var prodWeightRows = await _orderRepository.GetSalesProductWeightAggregatesForDashboardAsync(
            branchFilter,
            from,
            to,
            cancellationToken);
        var prodSales = await _orderRepository.GetSalesProductCategoryAggregatesForDashboardAsync(
            branchFilter,
            from,
            to,
            cancellationToken);

        var gramsByCategory = catWeightRows.ToDictionary(r => r.CategoryId, r => r.TotalWeightGrams);
        var gramsByProduct = prodWeightRows.ToDictionary(r => r.ProductId, r => r.TotalWeightGrams);

        var lines = new List<ExpenseMenuAttributionLineDto>();
        var allTargets = await _context.ExpenseMenuTargets
            .AsNoTracking()
            .Include(t => t.Expense)
            .ToListAsync(cancellationToken);

        if (allTargets.Count > 0)
        {
            var targetsGrouped = allTargets.GroupBy(t => t.ExpenseId).ToList();
            var expenseIds = targetsGrouped.Select(g => g.Key).ToList();

            var expenseTotals = await _expenseDashboardRepository.GetTotalsByExpenseCatalogIdsInRangeAsync(
                branchFilter,
                from,
                to,
                expenseIds,
                cancellationToken);

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
                nameByKey[new AttributionTargetKey(ExpenseMenuTargetType.ProductCategory, cid)] =
                    catNames.GetValueOrDefault(cid, $"#{cid}");
            foreach (var pid in allProdIds)
                nameByKey[new AttributionTargetKey(ExpenseMenuTargetType.Product, pid)] =
                    prodNames.GetValueOrDefault(pid, $"#{pid}");

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
        }

        var productCategoryMap = new Dictionary<int, int>();
        var productNameMap = new Dictionary<int, string>();
        foreach (var row in prodSales)
        {
            productCategoryMap[row.ProductId] = row.CategoryId;
            productNameMap[row.ProductId] = row.ProductName;
        }

        var prodIdsFromLines = lines
            .Where(l => l.TargetType == ExpenseMenuTargetType.Product)
            .Select(l => l.TargetId)
            .Where(id => !productCategoryMap.ContainsKey(id))
            .Distinct()
            .ToList();
        if (prodIdsFromLines.Count > 0)
        {
            var extra = await _context.Products
                .AsNoTracking()
                .Where(p => prodIdsFromLines.Contains(p.Id))
                .Select(p => new { p.Id, p.CategoryId, Name = p.Name ?? string.Empty })
                .ToListAsync(cancellationToken);
            foreach (var e in extra)
            {
                productCategoryMap[e.Id] = e.CategoryId;
                productNameMap[e.Id] = e.Name;
            }
        }

        var categoryIds = new HashSet<int>();
        foreach (var kv in gramsByCategory)
        {
            if (kv.Value > 0)
                categoryIds.Add(kv.Key);
        }

        foreach (var row in prodSales)
            categoryIds.Add(row.CategoryId);

        foreach (var line in lines)
        {
            if (line.TargetType == ExpenseMenuTargetType.ProductCategory)
                categoryIds.Add(line.TargetId);
            if (line.TargetType == ExpenseMenuTargetType.Product &&
                productCategoryMap.TryGetValue(line.TargetId, out var cid))
                categoryIds.Add(cid);
        }

        if (categoryIds.Count == 0)
        {
            return new MenuCategoryCostingDashboardResponseDto
            {
                FromUtc = from,
                ToUtc = to,
                BranchId = branchFilter,
                Categories = new List<MenuCategoryCostingBlockDto>(),
            };
        }

        var categoryNameById = await _context.ProductCategories
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name ?? $"#{c.Id}", cancellationToken);

        var blocks = new List<MenuCategoryCostingBlockDto>();
        foreach (var catId in categoryIds.OrderBy(id => categoryNameById.GetValueOrDefault(id, $"#{id}"), StringComparer.OrdinalIgnoreCase))
        {
            var catName = categoryNameById.GetValueOrDefault(catId, $"Categoría #{catId}");

            var catLines = lines.Where(l =>
                (l.TargetType == ExpenseMenuTargetType.ProductCategory && l.TargetId == catId)
                || (l.TargetType == ExpenseMenuTargetType.Product &&
                    productCategoryMap.GetValueOrDefault(l.TargetId) == catId)).ToList();

            var totalAllocated = catLines.Sum(l => l.AllocatedCop);
            var totalGrams = gramsByCategory.GetValueOrDefault(catId);
            decimal? blended = totalGrams > 0
                ? Math.Round((decimal)totalAllocated / totalGrams, 4, MidpointRounding.AwayFromZero)
                : null;

            var expenseBreakdown = catLines
                .GroupBy(l => l.ExpenseName)
                .Select(g => new MenuCategoryExpenseBreakdownDto
                {
                    ExpenseName = g.Key,
                    AllocatedCop = g.Sum(x => x.AllocatedCop),
                })
                .OrderByDescending(x => x.AllocatedCop)
                .ThenBy(x => x.ExpenseName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var categoryOnlyPool = lines
                .Where(l => l.TargetType == ExpenseMenuTargetType.ProductCategory && l.TargetId == catId)
                .Sum(l => l.AllocatedCop);

            var productIdsInCat = new HashSet<int>(
                prodSales.Where(p => p.CategoryId == catId).Select(p => p.ProductId));
            foreach (var l in lines.Where(l =>
                         l.TargetType == ExpenseMenuTargetType.Product &&
                         productCategoryMap.GetValueOrDefault(l.TargetId) == catId))
                productIdsInCat.Add(l.TargetId);

            var revenueByProduct = prodSales
                .Where(p => p.CategoryId == catId)
                .ToDictionary(p => p.ProductId, p => p.RevenueCop);

            long sumGramsWeighted = 0;
            long sumRevenue = 0;
            foreach (var pid in productIdsInCat)
            {
                var g = gramsByProduct.GetValueOrDefault(pid);
                if (g > 0)
                    sumGramsWeighted += g;
                var rev = revenueByProduct.GetValueOrDefault(pid);
                if (rev > 0)
                    sumRevenue += rev;
            }

            var productRows = new List<MenuProductCostingRowDto>();
            foreach (var pid in productIdsInCat.OrderBy(id => productNameMap.GetValueOrDefault(id, $"#{id}"), StringComparer.OrdinalIgnoreCase))
            {
                var grams = gramsByProduct.GetValueOrDefault(pid);
                var revenue = revenueByProduct.GetValueOrDefault(pid);
                var direct = lines
                    .Where(l => l.TargetType == ExpenseMenuTargetType.Product && l.TargetId == pid)
                    .Sum(l => l.AllocatedCop);

                decimal sharedFraction;
                if (categoryOnlyPool <= 0)
                    sharedFraction = 0;
                else if (sumGramsWeighted > 0 && grams > 0)
                    sharedFraction = (decimal)grams / sumGramsWeighted;
                else if (sumRevenue > 0 && revenue > 0)
                    sharedFraction = (decimal)revenue / sumRevenue;
                else if (productIdsInCat.Count > 0)
                    sharedFraction = 1m / productIdsInCat.Count;
                else
                    sharedFraction = 0;

                var shared = (long)Math.Round(categoryOnlyPool * sharedFraction, 0, MidpointRounding.AwayFromZero);
                var totalCost = direct + shared;

                decimal? avgPricePerGram = grams > 0 && revenue > 0
                    ? Math.Round((decimal)revenue / grams, 4, MidpointRounding.AwayFromZero)
                    : null;

                decimal? margin = revenue > 0
                    ? Math.Round((decimal)(revenue - totalCost) / revenue * 100, 1, MidpointRounding.AwayFromZero)
                    : null;

                productRows.Add(new MenuProductCostingRowDto
                {
                    ProductId = pid,
                    ProductName = productNameMap.GetValueOrDefault(pid, $"Producto #{pid}"),
                    RevenueCop = revenue,
                    GramsSold = grams,
                    AvgPricePerGramCop = avgPricePerGram,
                    AllocatedCostCop = totalCost,
                    MarginPercent = margin,
                });
            }

            var totalRevenueCat = prodSales.Where(p => p.CategoryId == catId).Sum(p => p.RevenueCop);

            blocks.Add(new MenuCategoryCostingBlockDto
            {
                CategoryId = catId,
                CategoryName = catName,
                TotalAllocatedCostCop = totalAllocated,
                TotalWeightGramsSold = totalGrams,
                BlendedCostPerGramCop = blended,
                TotalRevenueCop = totalRevenueCat,
                ExpenseBreakdown = expenseBreakdown,
                Products = productRows,
            });
        }

        return new MenuCategoryCostingDashboardResponseDto
        {
            FromUtc = from,
            ToUtc = to,
            BranchId = branchFilter,
            Categories = blocks,
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
