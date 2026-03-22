using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardCategoryWeightsHandler
    : IRequestHandler<GetDashboardCategoryWeightsQuery, DashboardCategoryWeightsResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IOrderRepository _orderRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardCategoryWeightsHandler(
        IOrderRepository orderRepository,
        IProductCategoryRepository categoryRepository,
        ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _categoryRepository = categoryRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardCategoryWeightsResponseDto> Handle(
        GetDashboardCategoryWeightsQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(request.FromUtc, request.ToUtc);
        var branchFilter = ResolveBranchFilter(request.BranchId);
        var granularity = ParseGranularity(request.Granularity);

        var agg = await _orderRepository.GetSalesCategoryWeightAggregatesForDashboardAsync(
            branchFilter,
            from,
            to,
            cancellationToken);

        var byCategory = agg
            .Select(r => new SalesCategoryWeightItemDto
            {
                CategoryId = r.CategoryId,
                Name = string.IsNullOrWhiteSpace(r.CategoryName) ? $"#{r.CategoryId}" : r.CategoryName,
                TotalWeightGrams = r.TotalWeightGrams,
            })
            .ToList();

        List<CategoryWeightEvolutionPointDto> evolution = new();
        List<CategoryWeightEvolutionSeriesDto> evolutionsByCategory = new();
        if (request.CategoryId is { } cid)
        {
            await ValidateCategoryAsync(cid, branchFilter);
            var points = await _orderRepository.GetSalesCategoryWeightEvolutionAsync(
                branchFilter,
                from,
                to,
                cid,
                granularity,
                cancellationToken);
            evolution = points
                .Select(p => new CategoryWeightEvolutionPointDto
                {
                    BucketStartUtc = p.BucketStartUtc,
                    TotalWeightGrams = p.TotalWeightGrams,
                })
                .ToList();
        }
        else
        {
            var seriesList = await _orderRepository.GetSalesCategoryWeightEvolutionAllCategoriesAsync(
                branchFilter,
                from,
                to,
                granularity,
                cancellationToken);
            evolutionsByCategory = seriesList
                .Select(s => new CategoryWeightEvolutionSeriesDto
                {
                    CategoryId = s.CategoryId,
                    Name = s.CategoryName,
                    Points = s.Points
                        .Select(p => new CategoryWeightEvolutionPointDto
                        {
                            BucketStartUtc = p.BucketStartUtc,
                            TotalWeightGrams = p.TotalWeightGrams,
                        })
                        .ToList(),
                })
                .ToList();
        }

        return new DashboardCategoryWeightsResponseDto
        {
            ByCategory = byCategory,
            Evolution = evolution,
            EvolutionsByCategory = evolutionsByCategory,
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

    private static CategoryWeightEvolutionGranularity ParseGranularity(string? g)
    {
        if (string.Equals(g, "month", StringComparison.OrdinalIgnoreCase))
            return CategoryWeightEvolutionGranularity.Month;
        if (string.Equals(g, "year", StringComparison.OrdinalIgnoreCase))
            return CategoryWeightEvolutionGranularity.Year;
        return CategoryWeightEvolutionGranularity.Day;
    }

    private async Task ValidateCategoryAsync(int categoryId, int? branchFilter)
    {
        var category = await _categoryRepository.GetByIdAsync(categoryId);
        if (category == null)
            throw new BusinessException("La categoría especificada no existe");

        if (_currentUser.Role != "superadmin" && category.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para consultar esta categoría");

        if (branchFilter.HasValue && category.BranchId != branchFilter.Value)
            throw new BusinessException("La categoría no corresponde a la sucursal seleccionada");
    }
}
