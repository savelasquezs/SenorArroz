using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Common;

namespace SenorArroz.Tests;

public class OrderUpdateGraphForPersistenceTests
{
    [Fact]
    public void DetachReadOnlyNavigations_clears_lookups_and_line_products_preserving_fk_scalars()
    {
        var branch = new Branch
        {
            Name = "B",
            Address = "A",
            Phone1 = "1",
            CreatedAt = DateTime.UtcNow
        };
        var takenBy = new User
        {
            Name = "U",
            Email = "e@test",
            PasswordHash = "h",
            Role = UserRole.Cashier,
            Branch = branch,
            CreatedAt = DateTime.UtcNow
        };
        var product = new Product
        {
            Name = "P",
            Price = 1000,
            Category = new ProductCategory { Name = "C", Branch = branch }
        };
        var line = new OrderDetail
        {
            Product = product,
            ProductId = 99,
            Quantity = 1,
            UnitPrice = 1000
        };
        var order = new Order
        {
            Branch = branch,
            BranchId = 7,
            TakenBy = takenBy,
            TakenById = 2,
            OrderDetails = [line]
        };

        OrderUpdateGraphForPersistence.DetachReadOnlyNavigations(order);

        Assert.Null(order.Branch);
        Assert.Null(order.TakenBy);
        Assert.Equal(7, order.BranchId);
        Assert.Equal(2, order.TakenById);
        Assert.Null(line.Product);
        Assert.Equal(99, line.ProductId);
    }
}
