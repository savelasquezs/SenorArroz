using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Tests;

public sealed class RappiOrderProcessorTests
{
    private const string ValidOrder = """
        {
          "order_detail": {
            "order_id": "rappi-order-1",
            "delivery_method": "delivery",
            "payment_method": "rappi_pay",
            "cooking_time": 25,
            "delivery_information": {
              "complete_address": "Calle 10 # 20-30"
            },
            "totals": {
              "total_order": 16000,
              "total_products": 18000,
              "total_discounts": 3000,
              "total_discount_by_partner": 1000,
              "charges": {
                "shipping": 1000
              }
            },
            "items": [
              {
                "id": "rappi-product-26",
                "sku": "product-26",
                "name": "Arroz paisa Personal",
                "type": "PRODUCT",
                "quantity": 1,
                "price": 18000,
                "comments": "Sin cebolla",
                "subitems": []
              }
            ],
            "discounts": [
              {
                "title": "Growth",
                "description": "Descuento compartido",
                "type": "campaign",
                "sku": "product-26",
                "value": 3000,
                "amount_by_rappi": 2000,
                "amount_by_partner": 1000
              }
            ]
          },
          "customer": {
            "first_name": "Cliente",
            "last_name": "Sandbox",
            "phone_number": "3000000000"
          },
          "store": {
            "internal_id": "900173116",
            "external_id": "900173116"
          }
        }
        """;

    private const string PosTesterOrder = """
        {
          "customer": {
            "first_name": "Usuario de prueba"
          },
          "order_detail": {
            "order_id": "pos-tester-order",
            "delivery_method": "delivery",
            "delivery_operation_type": "regular",
            "payment_method": "credit_card",
            "cooking_time": 30,
            "delivery_information": null,
            "totals": {
              "charges": {
                "shipping": 3000.00,
                "service_fee": 5900.00
              },
              "total_order": 18000.00,
              "total_to_pay": 0.00,
              "total_products": 18000,
              "total_discounts": 3000,
              "total_discount_by_partner": 0,
              "total_products_with_discount": 18000.00,
              "total_products_without_discount": 18000.00
            },
            "items": [
              {
                "id": "rappi-product-26",
                "sku": "product-26",
                "name": "Arroz paisa Personal",
                "type": "product",
                "quantity": 1,
                "price": 18000.00,
                "unit_price_with_discount": 18000.00,
                "unit_price_without_discount": 18000.00,
                "comments": "",
                "subitems": []
              }
            ],
            "discounts": [
              {
                "title": "Envio gratis",
                "description": "Descuento de envío del POS Tester",
                "type": "free_shipping",
                "value": 3000,
                "amount_by_rappi": 0,
                "amount_by_partner": 0
              }
            ]
          },
          "store": {
            "internal_id": "900173116",
            "external_id": "900173116",
            "name": "Señor Arroz Dev1"
          }
        }
        """;

    [Fact]
    public async Task Valid_order_is_accepted_once_and_preserves_growth_totals()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var rappi = new Mock<IRappiDeliveryProvider>();
        rappi.Setup(x => x.AcceptOrderAsync("rappi-order-1", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        var processor = CreateProcessor(db, rappi.Object);

        var first = await processor.IngestNewOrderAsync(1, ValidOrder, CancellationToken.None);
        var repeated = await processor.IngestNewOrderAsync(1, ValidOrder, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(repeated.Success);
        Assert.Equal(first.InternalOrderId, repeated.InternalOrderId);
        rappi.Verify(
            x => x.AcceptOrderAsync("rappi-order-1", 25, It.IsAny<CancellationToken>()),
            Times.Once);

        var external = await db.ExternalDeliveryOrders.SingleAsync();
        Assert.Equal(ExternalOrderStatus.Accepted, external.Status);
        Assert.Equal(16000, external.Total);
        Assert.Equal(18000, external.TotalProducts);
        Assert.Equal(3000, external.TotalDiscounts);
        Assert.Equal(1000, external.TotalDiscountByPartner);
        Assert.Equal(2000, external.TotalDiscountByRappi);
        Assert.Equal(1000, external.TotalCharges);

        var order = await db.Orders.Include(x => x.OrderDetails).SingleAsync();
        Assert.Equal("rappi-order-1", order.ExternalOrderId);
        Assert.Equal("rappi", order.OrderSource);
        Assert.Equal("rappi", order.ExternalFulfillmentProvider);
        Assert.Equal(OrderStatus.Taken, order.Status);
        Assert.Equal(16000, order.Total);
        Assert.Equal(18000, order.Subtotal);
        Assert.Equal(1000, order.DiscountTotal);
        Assert.Equal(2000, order.ExternalDiscountByRappi);
        Assert.Equal(1000, order.ExternalDiscountByPartner);
        Assert.Equal(1000, order.ExternalCharges);
        Assert.Single(order.OrderDetails);
        Assert.Equal(26, order.OrderDetails.Single().ProductId);

        var payment = await db.AppPayments.SingleAsync();
        Assert.Equal(16000, payment.Amount);
        Assert.Equal(4000m, payment.EstimatedCommissionAmount);
        Assert.Equal(12000m, payment.ExpectedNetAmount);
        Assert.False(payment.IsSetted);
    }

    [Fact]
    public async Task Price_mismatch_is_held_without_accepting_or_creating_an_order()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var rappi = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var processor = CreateProcessor(db, rappi.Object);
        var invalid = ValidOrder.Replace("\"price\": 18000", "\"price\": 17000");

        var result = await processor.IngestNewOrderAsync(1, invalid, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Held);
        Assert.Contains("precio distinto", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ExternalOrderStatus.BlockedMapping, (await db.ExternalDeliveryOrders.SingleAsync()).Status);
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.AppPayments.ToListAsync());
        rappi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Pos_tester_order_accepts_decimal_money_and_optional_rappi_delivery_address()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var rappi = new Mock<IRappiDeliveryProvider>();
        rappi.Setup(x => x.AcceptOrderAsync("pos-tester-order", 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        var processor = CreateProcessor(db, rappi.Object);

        var result = await processor.IngestNewOrderAsync(1, PosTesterOrder, CancellationToken.None);

        Assert.True(result.Success);
        var external = await db.ExternalDeliveryOrders.SingleAsync();
        Assert.Equal(ExternalOrderStatus.Accepted, external.Status);
        Assert.Equal(string.Empty, external.DeliveryAddress);
        Assert.Equal(18000, external.Total);
        Assert.Equal(18000, external.TotalProducts);
        Assert.Equal(3000, external.TotalDiscounts);
        Assert.Equal(8900, external.TotalCharges);

        var order = await db.Orders.Include(x => x.OrderDetails).SingleAsync();
        Assert.Equal(18000, order.Total);
        Assert.Equal(18000, order.Subtotal);
        Assert.Equal(18000, order.OrderDetails.Single().UnitPrice);
        Assert.Equal(18000, (await db.AppPayments.SingleAsync()).Amount);
    }

    [Fact]
    public async Task Revalidation_refreshes_a_previously_blocked_payload_snapshot()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.ExternalDeliveryOrders.Add(new ExternalDeliveryOrder
        {
            Id = 77,
            ConnectionId = 1,
            BranchId = 1,
            StoreId = 1,
            ExternalOrderId = "pos-tester-order",
            ExternalStoreId = "900173116",
            DeliveryMethod = "delivery",
            PaymentMethod = "credit_card",
            Total = 0,
            TotalProducts = 18000,
            TotalDiscounts = 3000,
            TotalDiscountByPartner = 0,
            TotalCharges = 0,
            RawPayloadJson = PosTesterOrder,
            LinesJson = "[]",
            DiscountsJson = "[]",
            Status = ExternalOrderStatus.BlockedMapping
        });
        await db.SaveChangesAsync();
        var rappi = new Mock<IRappiDeliveryProvider>();
        rappi.Setup(x => x.AcceptOrderAsync("pos-tester-order", 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        var processor = CreateProcessor(db, rappi.Object);

        var result = await processor.RevalidateAndAcceptAsync(77, 23, CancellationToken.None);

        Assert.True(result.Success);
        var external = await db.ExternalDeliveryOrders.SingleAsync();
        Assert.Equal(ExternalOrderStatus.Accepted, external.Status);
        Assert.Equal(18000, external.Total);
        Assert.Equal(8900, external.TotalCharges);
        Assert.Contains("18000", external.LinesJson, StringComparison.Ordinal);
        Assert.Equal(18000, (await db.Orders.Include(x => x.OrderDetails).SingleAsync())
            .OrderDetails.Single().UnitPrice);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var branch = new Branch { Id = 1, Name = "Santander" };
        var user = new User
        {
            Id = 23,
            BranchId = 1,
            Branch = branch,
            Role = UserRole.Cashier,
            Name = "Integración Rappi",
            Email = "integracion-rappi@senorarroz.internal"
        };
        var customer = new Customer
        {
            Id = 25714,
            BranchId = 1,
            Branch = branch,
            Name = "Rappi"
        };
        var bank = new Bank { Id = 2, BranchId = 1, Branch = branch, Name = "Rappi" };
        var app = new App { Id = 2, BankId = 2, Bank = bank, Name = "Rappi" };
        var category = new ProductCategory { Id = 1, BranchId = 1, Branch = branch, Name = "Paisa" };
        var product = new Product
        {
            Id = 26,
            CategoryId = 1,
            Category = category,
            Name = "Arroz paisa Personal",
            Price = 18000,
            Stock = 100,
            Active = true
        };
        var connection = new DeliveryAppConnection
        {
            Id = 1,
            BranchId = 1,
            Branch = branch,
            Provider = "rappi",
            IsActive = true,
            IsVerified = true,
            FinancialAppId = 2,
            FinancialApp = app,
            CustomerId = 25714,
            Customer = customer,
            TechnicalUserId = 23,
            TechnicalUser = user,
            DefaultCookingTimeMinutes = 30,
            EstimatedCommissionRate = 0.25m
        };
        connection.Stores.Add(new DeliveryAppStore
        {
            Id = 1,
            ConnectionId = 1,
            Connection = connection,
            RappiStoreId = "900173116",
            StoreIntegrationId = "900173116",
            Name = "Señor Arroz Dev1",
            IsParent = true
        });
        connection.ProductMappings.Add(new DeliveryAppProductMapping
        {
            Id = 1,
            ConnectionId = 1,
            Connection = connection,
            ProductId = 26,
            Product = product,
            Sku = "product-26",
            CategorySku = "category-1",
            IsSelected = true,
            PublishedPrice = 18000,
            PublishedAt = DateTime.UtcNow
        });
        db.DeliveryAppConnections.Add(connection);
        await db.SaveChangesAsync();
    }

    private static RappiOrderProcessor CreateProcessor(
        ApplicationDbContext db,
        IRappiDeliveryProvider rappi)
    {
        var orderRepository = new Mock<IOrderRepository>();
        orderRepository.Setup(x => x.GetByIdWithFullDetailsAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);
        return new RappiOrderProcessor(
            db,
            rappi,
            new FakeClock(new DateTime(2026, 8, 13, 20, 0, 0, DateTimeKind.Utc)),
            orderRepository.Object,
            Mock.Of<IMapper>(),
            Mock.Of<IOrderNotificationService>(),
            NullLogger<RappiOrderProcessor>.Instance);
    }
}
