using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class RappiGroupedMenuTests
{
    [Fact]
    public void Provider_menu_accepts_integral_decimal_prices_without_changing_values_or_choices()
    {
        var json = JsonSerializer.Serialize(Menu(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Replace("\"price\":18000", "\"price\":18000.0")
            .Replace("\"price\":13000", "\"price\":13000.00")
            .Replace("\"price\":12000", "\"price\":12000.000");
        var parsed = RappiCatalogMenu.Read(json);
        Assert.NotNull(parsed);
        Assert.True(RappiCatalogMenu.Matches(Menu(), parsed));
        Assert.Equal(18000, parsed.Items[0].Price);
        Assert.Equal(13000, parsed.Items[0].Children[1].Price);
    }

    [Theory]
    [InlineData("18000.5")]
    [InlineData("2147483648.0")]
    public void Provider_menu_rejects_fractional_or_out_of_range_prices(string price)
    {
        var json = JsonSerializer.Serialize(Menu(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Replace("\"price\":18000", "\"price\":" + price);
        Assert.Throws<JsonException>(() => RappiCatalogMenu.Read(json));
    }

    internal static RappiMenuRequest Menu() => new("900173116",
    [
        new(new("rice", 0, 0, "Especialidades", 0),
        [
            new(new("sizes", 1, 1, "Elige tu Tamaño", 0), [], "Personal", "", 0, "size-product-26", 0, "TOPPING", null, 1),
            new(new("sizes", 1, 1, "Elige tu Tamaño", 0), [], "Dúo", "", 13000, "size-product-27", 1, "TOPPING", null, 1)
        ], "Arroz Paisa", "Descripción del arroz", 18000, "product-26", 0, "PRODUCT", null),
        new(new("combos", 0, 0, "Combos", 1),
        [new(new("extras", 15, 0, "Adiciones", 0), [], "Chicharrón", "", 12000, "addon-product-39", 0, "TOPPING", null, 5)],
            "Combo", "Descripción del combo", 25000, "product-37", 0, "PRODUCT", null)
    ]);

    internal static async Task SeedProductsAsync(ApplicationDbContext db)
    {
        var connection = await db.DeliveryAppConnections.Include(x => x.ProductMappings).ThenInclude(x => x.Product).SingleAsync();
        var category = await db.ProductCategories.SingleAsync();
        foreach (var (id, name, price) in new[] { (27, "Paisa Dúo", 31000), (37, "Combo", 25000), (39, "Chicharrón", 15000) })
        {
            var product = new Product { Id = id, TenantId = connection.TenantId, Category = category, Name = name, Price = price, Stock = 100, Active = true };
            db.Products.Add(product);
            db.DeliveryAppProductMappings.Add(new DeliveryAppProductMapping
            {
                Id = id, TenantId = connection.TenantId, Connection = connection,
                Product = product, Sku = $"product-{id}", CategorySku = "category-1", IsSelected = true,
                PublishedPrice = price, PublishedAt = RappiCatalogTestData.Now
            });
        }
        await db.SaveChangesAsync();
    }

    internal static IReadOnlyList<ExternalDeliveryOrderLine> Lines() =>
    [
        new("rice", "product-26", "Arroz Paisa", "PRODUCT", 2, 18000, "Sin cebolla",
            [new("size", "size-product-27", "Dúo", "TOPPING", 1, 13000, null)]),
        new("combo", "product-37", "Combo", "PRODUCT", 2, 25000, null,
            [new("extra", "addon-product-39", "Chicharrón", "TOPPING", 2, 12000, null)])
    ];

    [Fact]
    public void Sizes_replace_the_base_product_and_addons_preserve_context_price_and_parent_quantity()
    {
        var result = RappiCatalogMenu.Normalize(Menu(), Lines());
        Assert.Empty(result.Errors);
        Assert.Collection(result.Lines,
            x => { Assert.Equal("product-27", x.Sku); Assert.Equal(2, x.Quantity); Assert.Equal(31000, x.UnitPrice); Assert.Equal("Sin cebolla", x.Notes); },
            x => { Assert.Equal("product-37", x.Sku); Assert.Equal(2, x.Quantity); Assert.Equal(25000, x.UnitPrice); },
            x => { Assert.Equal("product-39", x.Sku); Assert.Equal(4, x.Quantity); Assert.Equal(12000, x.UnitPrice); Assert.Equal(12000, x.ApprovedUnitPrice); });
        Assert.Equal(160000, result.Lines.Sum(x => x.Quantity * x.UnitPrice));
        Assert.All(result.Lines, x => Assert.Null(x.Subitems));
    }

    [Theory]
    [InlineData("TOPPING")]
    [InlineData("product")]
    public void Documented_subitem_types_resolve_only_within_their_approved_parent(string itemType)
    {
        var lines = Lines().ToList();
        lines[0] = lines[0] with { Subitems = [lines[0].Subitems![0] with { ItemType = itemType }] };
        Assert.Empty(RappiCatalogMenu.Normalize(Menu(), lines).Errors);
    }

    [Theory]
    [InlineData("missing-size")]
    [InlineData("two-sizes")]
    [InlineData("duplicate-size")]
    [InlineData("wrong-price")]
    [InlineData("wrong-base")]
    [InlineData("unknown-addon")]
    [InlineData("wrong-parent")]
    [InlineData("too-many")]
    [InlineData("nested")]
    public void Invalid_options_are_held_before_business_processing(string mutation)
    {
        var lines = Lines().ToList();
        var choice = lines[0].Subitems![0];
        switch (mutation)
        {
            case "missing-size": lines[0] = lines[0] with { Subitems = [] }; break;
            case "two-sizes": lines[0] = lines[0] with { Subitems = [choice, choice with { Sku = "size-product-26", UnitPrice = 0 }] }; break;
            case "duplicate-size": lines[0] = lines[0] with { Subitems = [choice, choice] }; break;
            case "wrong-price": lines[0] = lines[0] with { Subitems = [choice with { UnitPrice = 1 }] }; break;
            case "wrong-base": lines[0] = lines[0] with { UnitPrice = 1 }; break;
            case "unknown-addon": lines[1] = lines[1] with { Subitems = [lines[1].Subitems![0] with { Sku = "addon-product-999" }] }; break;
            case "wrong-parent": lines[0] = lines[0] with { Subitems = [choice, lines[1].Subitems![0]] }; break;
            case "too-many": lines[1] = lines[1] with { Subitems = [lines[1].Subitems![0] with { Quantity = 6 }] }; break;
            case "nested": lines[0] = lines[0] with { Subitems = [choice with { Subitems = [choice] }] }; break;
        }
        Assert.NotEmpty(RappiCatalogMenu.Normalize(Menu(), lines).Errors);
    }

    [Fact]
    public void Availability_keeps_the_group_open_when_another_size_is_available()
    {
        var result = RappiCatalogMenu.Availability(Menu(), new Dictionary<string, bool>
        { ["product-26"] = false, ["product-27"] = true }, new HashSet<string> { "product-26" });
        Assert.Contains("product-26", result.TurnOn);
        Assert.Contains("size-product-26", result.TurnOff);
        Assert.DoesNotContain("product-26", result.TurnOff);
    }

    [Fact]
    public async Task Draft_preserves_categories_and_choices_without_publishing_or_changing_internal_prices()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        await SeedProductsAsync(db);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var controller = RappiCatalogTestData.Controller(db, provider.Object);
        Assert.IsType<OkObjectResult>((await controller.SaveMenuDraft(1, Menu(), default)).Result);
        var draft = await db.RappiMenuPublications.SingleAsync();
        Assert.Equal("draft", draft.Status);
        using var json = JsonDocument.Parse(draft.PayloadJson);
        Assert.Equal(4, json.RootElement.GetProperty("posMappings").GetArrayLength());
        var preview = Assert.IsType<OkObjectResult>((await controller.PreviewMenu(1, default)).Result);
        var response = Assert.IsType<ApiResponse<object>>(preview.Value);
        var menu = Assert.IsType<RappiMenuRequest>(response.Data);
        Assert.Equal("Elige tu Tamaño", menu.Items[0].Children[0].Category.Name);
        Assert.Equal(15000, (await db.Products.SingleAsync(x => x.Id == 39)).Price);
        Assert.Empty(await db.Orders.ToListAsync());
        provider.VerifyNoOtherCalls();
    }

    internal static async Task VerifyBusinessFlowAsync(ApplicationDbContext db)
    {
        await SeedProductsAsync(db);
        var connection = await db.DeliveryAppConnections.Include(x => x.ProductMappings).ThenInclude(x => x.Product).SingleAsync();
        var snapshot = RappiCatalogMenu.Snapshot(Menu(), connection.ProductMappings);
        db.RappiMenuPublications.Add(new RappiMenuPublication
        {
            ConnectionId = connection.Id, StoreId = "900173116", PayloadJson = snapshot,
            Status = "approved", CompletedAt = RappiCatalogTestData.Now, CreatedAt = RappiCatalogTestData.Now
        });
        await db.SaveChangesAsync();
        var payload = JsonNode.Parse(RappiCatalogTestData.ValidOrder)!;
        var detail = payload["order_detail"]!;
        detail["totals"]!["total_order"] = 159000;
        detail["totals"]!["total_products"] = 160000;
        detail["items"] = JsonSerializer.SerializeToNode(Lines().Select(x => new
        {
            id = x.ExternalProductId, sku = x.Sku, name = x.Name, type = x.ItemType,
            quantity = x.Quantity, price = x.UnitPrice,
            subitems = x.Subitems!.Select(c => new { id = c.ExternalProductId, sku = c.Sku, name = c.Name, type = c.ItemType, quantity = c.Quantity, price = c.UnitPrice })
        }));
        var provider = new Mock<IRappiDeliveryProvider>();
        provider.Setup(x => x.AcceptOrderAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        var processor = RappiCatalogTestData.Processor(db, provider.Object);
        var result = await processor.IngestNewOrderAsync(connection.Id, payload.ToJsonString(), default);
        Assert.True(result.Success, result.Error);
        Assert.True((await processor.IngestNewOrderAsync(connection.Id, payload.ToJsonString(), default)).Success);
        var order = await db.Orders.Include(x => x.OrderDetails).SingleAsync();
        Assert.Equal(159000, order.Total);
        Assert.Equal(160000, order.Subtotal);
        Assert.DoesNotContain(order.OrderDetails, x => x.ProductId == 26);
        Assert.Contains(order.OrderDetails, x => x.ProductId == 27 && x.Quantity == 2 && x.UnitPrice == 31000);
        Assert.Contains(order.OrderDetails, x => x.ProductId == 39 && x.Quantity == 4 && x.UnitPrice == 12000);
        Assert.Single(await db.AppPayments.ToListAsync());
        provider.Verify(x => x.AcceptOrderAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Grouped_order_maps_real_products_and_preserves_growth_payment_once()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        await VerifyBusinessFlowAsync(db);
    }

    [Fact]
    public async Task Worker_synchronizes_published_choice_skus_and_keeps_other_sizes_visible()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        await SeedProductsAsync(db);
        var connection = await db.DeliveryAppConnections.Include(x => x.ProductMappings).ThenInclude(x => x.Product).SingleAsync();
        db.RappiMenuPublications.Add(new RappiMenuPublication
        {
            ConnectionId = 1, StoreId = "900173116", Status = "approved",
            PayloadJson = RappiCatalogMenu.Snapshot(Menu(), connection.ProductMappings), CompletedAt = RappiCatalogTestData.Now
        });
        foreach (var mapping in connection.ProductMappings)
            db.RappiAvailabilityStates.Add(new RappiAvailabilityState
            {
                ConnectionId = 1, StoreId = 1, ProductMappingId = mapping.Id,
                DesiredAvailable = mapping.ProductId != 26, Status = "pending"
            });
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.SetAvailabilityAsync(It.IsAny<IReadOnlyCollection<RappiAvailabilityRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(false, 503));

        await RappiCatalogTestData.WorkerAsync("ProcessAvailabilityAsync", db, provider.Object);

        provider.Verify(x => x.SetAvailabilityAsync(It.Is<IReadOnlyCollection<RappiAvailabilityRequest>>(r => r.Count == 1
            && r.Single().TurnOn.Contains("product-26") && r.Single().TurnOn.Contains("size-product-27")
            && r.Single().TurnOff.Contains("size-product-26") && r.Single().TurnOn.Contains("addon-product-39")),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.All(await db.RappiAvailabilityStates.ToListAsync(), x =>
        {
            Assert.Equal("pending", x.Status);
            Assert.Equal(1, x.AttemptCount);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Store_approval_does_not_confirm_a_different_or_empty_menu(bool matching)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        await SeedProductsAsync(db);
        var connection = await db.DeliveryAppConnections.Include(x => x.ProductMappings).ThenInclude(x => x.Product).SingleAsync();
        var publication = new RappiMenuPublication
        {
            ConnectionId = 1, StoreId = "900173116", Status = "submitted", CreatedAt = RappiCatalogTestData.Now,
            PayloadJson = RappiCatalogMenu.Snapshot(Menu(), connection.ProductMappings)
        };
        db.RappiMenuPublications.Add(publication);
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.GetMenuApprovalAsync("900173116", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        provider.Setup(x => x.GetMenuAsync("900173116", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiMenuResult(true, matching ? Menu() : new RappiMenuRequest("900173116", [])));

        await RappiCatalogTestData.WorkerAsync("RecoverMenuApprovalsAsync", db, provider.Object);

        Assert.Equal(matching ? 1 : 0, await db.IntegrationWebhookEvents.CountAsync());
        if (matching)
        {
            var approval = await db.IntegrationWebhookEvents.SingleAsync();
            await RappiCatalogTestData.ApplyMenuAsync(db, approval.EventType, approval.EventKey);
            var variant = await db.DeliveryAppProductMappings.SingleAsync(x => x.ProductId == 27);
            Assert.Equal("Paisa Dúo", variant.PublishedName);
            Assert.Equal(31000, variant.PublishedPrice);
        }
    }
}
