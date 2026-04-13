using Microsoft.EntityFrameworkCore;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class QueryablePagingExtensionsTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Returns_correct_page_and_totalCount()
    {
        using var ctx = CreateContext(nameof(Returns_correct_page_and_totalCount));
        for (var i = 1; i <= 10; i++)
            ctx.ProductCategories.Add(new Domain.Entities.ProductCategory { Name = $"Cat {i:D2}", BranchId = 1 });
        await ctx.SaveChangesAsync();

        var result = await ctx.ProductCategories
            .OrderBy(c => c.Name)
            .ToPagedResultAsync(page: 1, pageSize: 3);

        Assert.Equal(10, result.TotalCount);
        Assert.Equal(4, result.TotalPages);
        Assert.Equal(1, result.Page);
        Assert.Equal(3, result.PageSize);
        Assert.Equal(3, result.Items.Count());
    }

    [Fact]
    public async Task Last_page_returns_remaining_items()
    {
        using var ctx = CreateContext(nameof(Last_page_returns_remaining_items));
        for (var i = 1; i <= 10; i++)
            ctx.ProductCategories.Add(new Domain.Entities.ProductCategory { Name = $"Cat {i:D2}", BranchId = 1 });
        await ctx.SaveChangesAsync();

        var result = await ctx.ProductCategories
            .OrderBy(c => c.Name)
            .ToPagedResultAsync(page: 4, pageSize: 3);

        Assert.Equal(10, result.TotalCount);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task HasNextPage_and_HasPreviousPage_are_correct_for_middle_page()
    {
        using var ctx = CreateContext(nameof(HasNextPage_and_HasPreviousPage_are_correct_for_middle_page));
        for (var i = 1; i <= 9; i++)
            ctx.ProductCategories.Add(new Domain.Entities.ProductCategory { Name = $"Cat {i}", BranchId = 1 });
        await ctx.SaveChangesAsync();

        var result = await ctx.ProductCategories
            .OrderBy(c => c.Name)
            .ToPagedResultAsync(page: 2, pageSize: 3);

        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public async Task Page_beyond_total_returns_empty_items_but_correct_totalCount()
    {
        using var ctx = CreateContext(nameof(Page_beyond_total_returns_empty_items_but_correct_totalCount));
        for (var i = 1; i <= 5; i++)
            ctx.ProductCategories.Add(new Domain.Entities.ProductCategory { Name = $"Cat {i}", BranchId = 1 });
        await ctx.SaveChangesAsync();

        var result = await ctx.ProductCategories
            .OrderBy(c => c.Name)
            .ToPagedResultAsync(page: 99, pageSize: 10);

        Assert.Equal(5, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task TotalPages_rounds_up_correctly()
    {
        using var ctx = CreateContext(nameof(TotalPages_rounds_up_correctly));
        for (var i = 1; i <= 7; i++)
            ctx.ProductCategories.Add(new Domain.Entities.ProductCategory { Name = $"Cat {i}", BranchId = 1 });
        await ctx.SaveChangesAsync();

        var result = await ctx.ProductCategories
            .ToPagedResultAsync(page: 1, pageSize: 3);

        Assert.Equal(3, result.TotalPages);
    }
}
