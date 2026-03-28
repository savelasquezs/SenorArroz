using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Expenses.Services;

public readonly record struct AttributionTargetKey(ExpenseMenuTargetType TargetType, int TargetId);

public sealed record ExpenseMenuAttributionLine(
    int ExpenseId,
    string ExpenseName,
    long TotalExpenseInPeriodCop,
    ExpenseMenuTargetType TargetType,
    int TargetId,
    string TargetName,
    long AllocatedCop,
    long TotalWeightGramsSold,
    decimal? CostPerGramCop);

/// <summary>
/// Reparto proporcional del gasto del periodo según gramos vendidos por destino; destinos con 0 g no reciben parte (redistribución al resto).
/// </summary>
public static class ExpenseMenuAttributionCalculator
{
    public static IReadOnlyList<ExpenseMenuAttributionLine> BuildLines(
        int expenseId,
        string expenseName,
        long totalExpenseCop,
        IReadOnlyList<(ExpenseMenuTargetType Type, int Id)> targets,
        IReadOnlyDictionary<int, long> gramsByCategoryId,
        IReadOnlyDictionary<int, long> gramsByProductId,
        IReadOnlyDictionary<AttributionTargetKey, string> targetNames)
    {
        if (targets.Count == 0)
            return Array.Empty<ExpenseMenuAttributionLine>();

        var weights = new List<(AttributionTargetKey Key, long Grams)>(targets.Count);
        foreach (var (type, id) in targets)
        {
            var key = new AttributionTargetKey(type, id);
            long g = type switch
            {
                ExpenseMenuTargetType.ProductCategory => gramsByCategoryId.GetValueOrDefault(id, 0L),
                ExpenseMenuTargetType.Product => gramsByProductId.GetValueOrDefault(id, 0L),
                _ => 0L,
            };
            weights.Add((key, g));
        }

        var positive = weights.Where(w => w.Grams > 0).ToList();
        var totalPositiveGrams = positive.Sum(w => w.Grams);

        var lines = new List<ExpenseMenuAttributionLine>(weights.Count);
        foreach (var (key, grams) in weights)
        {
            var name = targetNames.GetValueOrDefault(key, key.TargetType == ExpenseMenuTargetType.ProductCategory
                ? $"Categoría #{key.TargetId}"
                : $"Producto #{key.TargetId}");

            if (grams <= 0 || totalPositiveGrams <= 0)
            {
                lines.Add(new ExpenseMenuAttributionLine(
                    expenseId,
                    expenseName,
                    totalExpenseCop,
                    key.TargetType,
                    key.TargetId,
                    name,
                    0L,
                    0L,
                    null));
                continue;
            }

            var allocated = (decimal)totalExpenseCop * grams / totalPositiveGrams;
            var allocatedLong = (long)Math.Round(allocated, 0, MidpointRounding.AwayFromZero);
            var perGram = (decimal)allocatedLong / grams;
            lines.Add(new ExpenseMenuAttributionLine(
                expenseId,
                expenseName,
                totalExpenseCop,
                key.TargetType,
                key.TargetId,
                name,
                allocatedLong,
                grams,
                Math.Round(perGram, 6, MidpointRounding.AwayFromZero)));
        }

        return lines;
    }
}
