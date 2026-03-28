using SenorArroz.Application.Features.Expenses.Services;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class ExpenseMenuAttributionCalculatorTests
{
    [Fact]
    public void BuildLines_TwoCategories_splits_by_grams()
    {
        var targets = new List<(ExpenseMenuTargetType, int)>
        {
            (ExpenseMenuTargetType.ProductCategory, 1),
            (ExpenseMenuTargetType.ProductCategory, 2),
        };
        var gramsCat = new Dictionary<int, long> { [1] = 700, [2] = 300 };
        var gramsProd = new Dictionary<int, long>();
        var names = new Dictionary<AttributionTargetKey, string>
        {
            [new(ExpenseMenuTargetType.ProductCategory, 1)] = "A",
            [new(ExpenseMenuTargetType.ProductCategory, 2)] = "B",
        };

        var lines = ExpenseMenuAttributionCalculator.BuildLines(
            10,
            "Carne",
            1_000_000L,
            targets,
            gramsCat,
            gramsProd,
            names);

        Assert.Equal(2, lines.Count);
        var a = lines.First(x => x.TargetId == 1);
        var b = lines.First(x => x.TargetId == 2);
        Assert.Equal(700_000L, a.AllocatedCop);
        Assert.Equal(300_000L, b.AllocatedCop);
        Assert.NotNull(a.CostPerGramCop);
        Assert.NotNull(b.CostPerGramCop);
    }

    [Fact]
    public void BuildLines_ZeroGrams_redistributes_to_positive()
    {
        var targets = new List<(ExpenseMenuTargetType, int)>
        {
            (ExpenseMenuTargetType.ProductCategory, 1),
            (ExpenseMenuTargetType.ProductCategory, 2),
        };
        var gramsCat = new Dictionary<int, long> { [1] = 0, [2] = 500 };
        var gramsProd = new Dictionary<int, long>();
        var names = new Dictionary<AttributionTargetKey, string>
        {
            [new(ExpenseMenuTargetType.ProductCategory, 1)] = "A",
            [new(ExpenseMenuTargetType.ProductCategory, 2)] = "B",
        };

        var lines = ExpenseMenuAttributionCalculator.BuildLines(
            1,
            "X",
            100L,
            targets,
            gramsCat,
            gramsProd,
            names);

        var a = lines.First(x => x.TargetId == 1);
        var b = lines.First(x => x.TargetId == 2);
        Assert.Equal(0L, a.AllocatedCop);
        Assert.Null(a.CostPerGramCop);
        Assert.Equal(100L, b.AllocatedCop);
        Assert.NotNull(b.CostPerGramCop);
    }

    [Fact]
    public void BuildLines_AllZeroGrams_no_cost_per_gram()
    {
        var targets = new List<(ExpenseMenuTargetType, int)>
        {
            (ExpenseMenuTargetType.Product, 5),
        };
        var gramsCat = new Dictionary<int, long>();
        var gramsProd = new Dictionary<int, long> { [5] = 0L };
        var names = new Dictionary<AttributionTargetKey, string>
        {
            [new(ExpenseMenuTargetType.Product, 5)] = "P",
        };

        var lines = ExpenseMenuAttributionCalculator.BuildLines(
            1,
            "X",
            50L,
            targets,
            gramsCat,
            gramsProd,
            names);

        var line = lines.Single();
        Assert.Equal(0L, line.AllocatedCop);
        Assert.Null(line.CostPerGramCop);
    }
}
