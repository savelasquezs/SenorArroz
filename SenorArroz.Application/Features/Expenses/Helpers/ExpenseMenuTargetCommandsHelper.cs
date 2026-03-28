using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Expenses.Helpers;

public static class ExpenseMenuTargetCommandsHelper
{
    public static void ValidateMenuTargetInputs(IReadOnlyList<ExpenseMenuTargetInputDto> items)
    {
        if (items == null || items.Count == 0)
            return;

        var seen = new HashSet<(ExpenseMenuTargetType, int)>();
        foreach (var item in items)
        {
            if (item.TargetId <= 0)
                throw new BusinessException("Cada destino de menú debe tener un id válido");

            if (!Enum.IsDefined(typeof(ExpenseMenuTargetType), item.TargetType))
                throw new BusinessException("Tipo de destino de menú no válido");

            if (!seen.Add((item.TargetType, item.TargetId)))
                throw new BusinessException("No se puede repetir el mismo destino de menú");
        }
    }

    public static async Task EnsureTargetsExistAsync(
        IReadOnlyList<ExpenseMenuTargetInputDto> items,
        IProductRepository productRepository,
        IProductCategoryRepository categoryRepository,
        CancellationToken cancellationToken)
    {
        if (items == null || items.Count == 0)
            return;

        foreach (var item in items)
        {
            switch (item.TargetType)
            {
                case ExpenseMenuTargetType.ProductCategory:
                    if (!await categoryRepository.ExistsAsync(item.TargetId))
                        throw new NotFoundException($"Categoría de producto #{item.TargetId} no encontrada");
                    break;
                case ExpenseMenuTargetType.Product:
                    if (!await productRepository.ExistsAsync(item.TargetId))
                        throw new NotFoundException($"Producto #{item.TargetId} no encontrado");
                    break;
                default:
                    throw new BusinessException("Tipo de destino de menú no soportado");
            }
        }
    }

    public static async Task ReplaceMenuTargetsAsync(
        int expenseId,
        IReadOnlyList<ExpenseMenuTargetInputDto> items,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        await context.ExpenseMenuTargets
            .Where(t => t.ExpenseId == expenseId)
            .ExecuteDeleteAsync(cancellationToken);

        if (items == null || items.Count == 0)
            return;

        foreach (var item in items)
        {
            context.ExpenseMenuTargets.Add(new ExpenseMenuTarget
            {
                ExpenseId = expenseId,
                TargetType = item.TargetType,
                TargetId = item.TargetId,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnrichMenuTargetsAsync(
        ExpenseDto dto,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        if (dto.MenuTargets.Count == 0)
            return;

        var catIds = dto.MenuTargets
            .Where(t => t.TargetType == ExpenseMenuTargetType.ProductCategory)
            .Select(t => t.TargetId)
            .Distinct()
            .ToList();
        var prodIds = dto.MenuTargets
            .Where(t => t.TargetType == ExpenseMenuTargetType.Product)
            .Select(t => t.TargetId)
            .Distinct()
            .ToList();

        var catNames = catIds.Count == 0
            ? new Dictionary<int, string>()
            : await context.ProductCategories
                .AsNoTracking()
                .Where(c => catIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name ?? $"#{c.Id}", cancellationToken);

        Dictionary<int, (string ProductName, int? WeightGrams)> products;
        if (prodIds.Count == 0)
            products = new Dictionary<int, (string, int?)>();
        else
        {
            var rows = await context.Products
                .AsNoTracking()
                .Where(p => prodIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.WeightGrams })
                .ToListAsync(cancellationToken);
            products = rows.ToDictionary(
                x => x.Id,
                x => (x.Name ?? $"#{x.Id}", x.WeightGrams));
        }

        foreach (var row in dto.MenuTargets)
        {
            switch (row.TargetType)
            {
                case ExpenseMenuTargetType.ProductCategory:
                    row.TargetName = catNames.GetValueOrDefault(row.TargetId, $"Categoría #{row.TargetId}");
                    row.ProductMissingWeight = false;
                    break;
                case ExpenseMenuTargetType.Product:
                    if (products.TryGetValue(row.TargetId, out var info))
                    {
                        row.TargetName = info.ProductName;
                        row.ProductMissingWeight = info.WeightGrams is null or <= 0;
                    }
                    else
                    {
                        row.TargetName = $"Producto #{row.TargetId}";
                        row.ProductMissingWeight = true;
                    }
                    break;
            }
        }
    }
}
