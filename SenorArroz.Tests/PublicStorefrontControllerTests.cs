using AutoMapper;
using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.API.Security;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class PublicStorefrontControllerTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Endpoints_RequireStorefrontAuthentication()
    {
        var authorization = Assert.Single(typeof(PublicStorefrontController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(StorefrontApiKeyOptions.Scheme, authorization.AuthenticationSchemes);
        Assert.Empty(typeof(PublicStorefrontController).GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task Catalog_ReturnsSharedActiveProductsOnlyOnce()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).GetCatalog(default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<PublicCatalogDto>>(ok.Value);
        var group = Assert.Single(response.Data!.RiceGroups);
        var product = Assert.Single(group.Options);
        Assert.Equal(50_000, product.Price);
        Assert.Equal("Arroces", group.CategoryName);
        Assert.Equal("available", product.AvailabilityStatus);
        Assert.Null(typeof(PublicProductOptionDto).GetProperty("Stock"));
        Assert.Equal(7, Assert.Single(response.Data.Branches).BusinessHours.Count);
    }

    [Fact]
    public async Task Catalog_ExposesAvailabilityWithoutExactInventory()
    {
        await using var db = CreateDb();
        Seed(db);
        db.ChangeTracker.Entries<Product>().Single(x => x.Entity.Id == 20).Entity.Stock = 3;
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).GetCatalog(default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<PublicCatalogDto>>(ok.Value);
        Assert.Equal("lowStock", Assert.Single(Assert.Single(response.Data!.RiceGroups).Options).AvailabilityStatus);
        Assert.Empty(response.Data.Promotions);
    }

    [Fact]
    public async Task Catalog_IncludesActiveBranchWithoutAiWhatsAppSetting_AndUsesMainPhone()
    {
        await using var db = CreateDb();
        Seed(db);
        db.Add(new Branch
        {
            Id = 15,
            Name = "La 80",
            Address = "Calle 80",
            Phone1 = "3017654321",
            Latitude = 6.27m,
            Longitude = -75.59m,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).GetCatalog(default);

        var response = Assert.IsType<ApiResponse<PublicCatalogDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal(2, response.Data!.Branches.Count);
        var branch = response.Data.Branches.Single(x => x.Id == 15);
        Assert.Equal("https://wa.me/573017654321", branch.ContactWhatsAppUrl);
        Assert.Equal(7, branch.BusinessHours.Count);
        Assert.All(branch.BusinessHours, hour => Assert.True(hour.IsClosed));
    }

    [Fact]
    public async Task Catalog_GroupsProductsByCommercialProfile_AndOrdersVariants()
    {
        await using var db = CreateDb();
        Seed(db);
        var category = db.ProductCategories.Local.Single();
        var profile = new CommercialProfile { Id = 30, BranchId = category.BranchId, Branch = category.Branch, Name = "Arroz paisa", Description = "Sabor de casa" };
        var first = db.Products.Local.Single();
        first.CommercialProfile = profile;
        first.CommercialProfileId = profile.Id;
        first.StorefrontVariantLabel = "Familiar";
        first.StorefrontSortOrder = 20;
        db.Add(new Product { Id = 21, CategoryId = category.Id, Category = category, Name = "Arroz paisa Personal", Price = 18_000, Stock = 10, Active = true, CommercialProfile = profile, CommercialProfileId = profile.Id, StorefrontVariantLabel = "Personal", StorefrontSortOrder = 30 });
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).GetCatalog(default);

        var response = Assert.IsType<ApiResponse<PublicCatalogDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        var group = Assert.Single(response.Data!.RiceGroups);
        Assert.Equal("Arroz paisa", group.Name);
        Assert.Equal(["Personal", "Familiar"], group.Options.Select(x => x.VariantLabel));
    }

    [Fact]
    public async Task Catalog_HidesOperationalCategories_AndUsesIndividualFallbackWithoutProfile()
    {
        await using var db = CreateDb();
        Seed(db);
        var branch = db.Branches.Local.Single();
        var hidden = new ProductCategory { Id = 13, BranchId = branch.Id, Branch = branch, Name = "Empaque", StorefrontRole = "hidden" };
        db.AddRange(hidden, new Product { Id = 22, CategoryId = hidden.Id, Category = hidden, Name = "Cuchara", Price = 0, Stock = 10, Active = true });
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).GetCatalog(default);

        var response = Assert.IsType<ApiResponse<PublicCatalogDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Single(response.Data!.RiceGroups);
        Assert.Empty(response.Data.ComboGroups);
        Assert.DoesNotContain(response.Data.RiceGroups, x => x.Name == "Cuchara");
        Assert.StartsWith("rice:product:", response.Data.RiceGroups.Single().Key);
    }

    [Fact]
    public async Task AddressPreview_ReturnsCoordinatesForCustomerConfirmation()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).PreviewAddress(new PublicAddressPreviewRequest
        {
            City = "Medellín",
            Address = "Calle 10 # 20-30",
        }, default);

        var response = Assert.IsType<ApiResponse<PublicAddressPreviewDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal(6.25m, response.Data!.Latitude);
        Assert.Equal(-75.56m, response.Data.Longitude);
        Assert.Contains("Medellín", response.Data.FormattedAddress);
    }

    [Fact]
    public async Task AddressPreview_RejectsUnsupportedCity()
    {
        await using var db = CreateDb();

        var action = await Controller(db, 1800).PreviewAddress(new PublicAddressPreviewRequest
        {
            City = "Bogotá",
            Address = "Calle 10 # 20-30",
        }, default);

        Assert.IsType<BadRequestObjectResult>(action.Result);
    }

    [Fact]
    public async Task Quote_RejectsCartWithoutMainProduct()
    {
        await using var db = CreateDb();
        Seed(db);
        db.ProductCategories.Local.Single().StorefrontRole = "addition";
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).Quote(Request(), default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<BadRequestObjectResult>(action.Result).Value);
        Assert.Contains("arroz o combo", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quote_RejectsProductFromHiddenCategory()
    {
        await using var db = CreateDb();
        Seed(db);
        db.ProductCategories.Local.Single().StorefrontRole = "hidden";
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).Quote(Request(), default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<BadRequestObjectResult>(action.Result).Value);
        Assert.Contains("no está habilitado", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quote_Pickup_DoesNotRequireAddressAndUsesSelectedBranch()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();
        var request = Request();
        request.FulfillmentType = "pickup";
        request.City = null;
        request.Address = null;
        request.Latitude = null;
        request.Longitude = null;
        request.SelectedBranchId = 10;

        var action = await Controller(db, 1800).Quote(request, default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("pickup", response.Data!.FulfillmentType);
        Assert.Null(response.Data.FormattedAddress);
        Assert.Equal(0, response.Data.EstimatedDeliveryFee);
        Assert.All(response.Data.Branches, branch => Assert.Null(branch.RoutePolyline));
        Assert.StartsWith("https://wa.me/573001234567?text=", response.Data.WhatsAppUrl);
        var message = Uri.UnescapeDataString(response.Data.WhatsAppUrl);
        Assert.Contains("Recoger en el local", message);
        Assert.Contains("Calle 1", message);
        Assert.DoesNotContain("Autorización", message);
    }

    [Fact]
    public async Task Quote_RejectsPickupWhenBranchIsClosed()
    {
        await using var db = CreateDb();
        Seed(db);
        var sunday = db.BranchBusinessHours.Local.Single(x => x.DayOfWeek == DayOfWeek.Sunday);
        sunday.IsClosed = true;
        sunday.OpenTime = null;
        sunday.CloseTime = null;
        await db.SaveChangesAsync();
        var request = Request();
        request.FulfillmentType = "pickup";
        request.City = null;
        request.Address = null;
        request.Latitude = null;
        request.Longitude = null;
        request.SelectedBranchId = 10;

        var action = await Controller(db, 1800).Quote(request, default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<ConflictObjectResult>(action.Result).Value);
        Assert.Contains("fuera de su horario", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quote_RejectsDeliveryWhenNoBranchHasConfiguredHours()
    {
        await using var db = CreateDb();
        Seed(db);
        db.BranchBusinessHours.RemoveRange(db.BranchBusinessHours.Local);
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).Quote(Request(), default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<ConflictObjectResult>(action.Result).Value);
        Assert.Contains("horario válido", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quote_DeliveryUsesOnlyOpenBranches()
    {
        await using var db = CreateDb();
        Seed(db);
        var closedSunday = db.BranchBusinessHours.Local.Single(x => x.BranchId == 10 && x.DayOfWeek == DayOfWeek.Sunday);
        closedSunday.IsClosed = true;
        closedSunday.OpenTime = null;
        closedSunday.CloseTime = null;
        var openBranch = new Branch { Id = 15, Name = "La 80", Address = "Calle 80", Phone1 = "3017654321", Latitude = 6.27m, Longitude = -75.59m, IsActive = true };
        db.Branches.Add(openBranch);
        db.BranchBusinessHours.AddRange(Enum.GetValues<DayOfWeek>().Select((day, index) => new BranchBusinessHour
        {
            BranchId = openBranch.Id,
            Branch = openBranch,
            DayOfWeek = day,
            OpenTime = new TimeOnly(8, 0),
            CloseTime = new TimeOnly(22, 0),
            DisplayOrder = index,
        }));
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).Quote(Request(), default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal(15, response.Data!.CheckoutBranchId);
        Assert.Equal(15, Assert.Single(response.Data.Branches).Id);
    }

    [Fact]
    public async Task Quote_RejectsFinalValidationAfterBranchCloses()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();
        var first = await Controller(db, 1800).Quote(Request(), default);
        Assert.IsType<OkObjectResult>(first.Result);
        var sunday = db.BranchBusinessHours.Single(x => x.BranchId == 10 && x.DayOfWeek == DayOfWeek.Sunday);
        sunday.IsClosed = true;
        sunday.OpenTime = null;
        sunday.CloseTime = null;
        await db.SaveChangesAsync();

        var second = await Controller(db, 1800).Quote(Request(), default);

        Assert.IsType<ConflictObjectResult>(second.Result);
    }

    [Theory]
    [InlineData("+57 300 123 4567")]
    [InlineData("0057 300-123-4567")]
    public async Task Quote_NormalizesColombianMobileBeforeValidationAndWhatsApp(string phone)
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();
        var request = Request();
        request.Phone = phone;

        var action = await Controller(db, 1800).Quote(request, default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("3001234567", request.Phone);
        Assert.Contains("*Teléfono:* 3001234567", Uri.UnescapeDataString(response.Data!.WhatsAppUrl));
    }

    [Theory]
    [InlineData("300123456")]
    [InlineData("2001234567")]
    public async Task Quote_RejectsInvalidColombianMobile(string phone)
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();
        var request = Request();
        request.Phone = phone;

        var action = await Controller(db, 1800).Quote(request, default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<BadRequestObjectResult>(action.Result).Value);
        Assert.Contains("celular colombiano", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quote_RequiresDistanceAndTravelLimits_AndReturnsSafeDeliveryPromise()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).Quote(Request(), default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(ok.Value);
        Assert.False(response.Data!.IsOutsideCoverage);
        Assert.Equal(30, response.Data.TravelMinutes);
        Assert.Equal(4_000, response.Data.DistanceMeters);
        Assert.Equal(5_000, response.Data.EstimatedDeliveryFee);
        Assert.Equal(response.Data.Subtotal + 5_000, response.Data.Total);
        Assert.Equal(20, response.Data.PreparationMinutes);
        Assert.Equal(45, response.Data.EstimatedTotalMinutes);
        Assert.Equal(35, response.Data.DeliveryPromiseMinMinutes);
        Assert.Equal(45, response.Data.DeliveryPromiseMaxMinutes);
        Assert.Equal("encoded-route", Assert.Single(response.Data.Branches).RoutePolyline);
        var message = Uri.UnescapeDataString(response.Data.WhatsAppUrl);
        Assert.Contains("Valor estimado del domicilio", message);
        Assert.Contains("*Subtotal:*", message);
        Assert.Contains("*Domicilio:*", message);
        Assert.Contains("*Total estimado:*", message);
        Assert.DoesNotContain("Autorización", message);
        Assert.Contains("Torre A, apartamento 202", message);
    }

    [Fact]
    public async Task Quote_OverThirtyTravelMinutes_RequiresAuthorization()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();

        var action = await Controller(db, 1801).Quote(Request(), default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(ok.Value);
        Assert.True(response.Data!.IsOutsideCoverage);
        Assert.Equal(31, response.Data.TravelMinutes);
        Assert.Equal(45, response.Data.EstimatedTotalMinutes);
        var message = Uri.UnescapeDataString(response.Data.WhatsAppUrl);
        Assert.Contains("*NUEVO PEDIDO WEB*", message);
        Assert.DoesNotContain("fuera de cobertura", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quote_OverFiveKilometers_IsOutsideCoverageEvenUnderThirtyMinutes()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();

        var action = await Controller(db, 1200, 5_001).Quote(Request(), default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.True(response.Data!.IsOutsideCoverage);
        Assert.Equal(7_000, response.Data.EstimatedDeliveryFee);
    }

    [Fact]
    public async Task Quote_AppliesEligibleDailyPromotionToServerCalculatedTotal()
    {
        await using var db = CreateDb();
        Seed(db);
        var branch = db.Branches.Local.Single();
        db.DailyPromotions.Add(new DailyPromotion
        {
            BranchId = branch.Id,
            Branch = branch,
            Type = DailyPromotionType.PercentageDiscount,
            DiscountPercentage = 10,
            DiscountScope = DailyPromotionDiscountScope.AllProducts,
            IsActive = true,
            StartsAt = Now.AddHours(-1),
            EndsAt = Now.AddHours(1),
        });
        await db.SaveChangesAsync();

        var action = await Controller(db, 720).Quote(Request(), default);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("daily_promotion", response.Data!.AppliedBenefit!.Source);
        Assert.Equal(10_000, response.Data.DiscountTotal);
        Assert.Equal(95_000, response.Data.Total);
        Assert.All(response.Data.Items, line => Assert.Equal(line.Quantity * line.UnitPrice - line.Discount, line.Subtotal));
    }

    [Fact]
    public async Task Quote_RequiresCustomerChoiceWhenPromotionAndLoyaltyRewardCompete()
    {
        await using var db = CreateDb();
        Seed(db);
        var branch = db.Branches.Local.Single();
        var customer = new Customer { Id = 77, BranchId = branch.Id, Branch = branch, Name = "Cliente fiel", Phone1 = "3001234567", Active = true };
        db.Customers.Add(customer);
        for (var index = 0; index < 4; index++)
            db.Orders.Add(new Order { BranchId = branch.Id, TakenById = 1, Customer = customer, Status = OrderStatus.Delivered, Type = OrderType.Onsite, Subtotal = 10_000, Total = 10_000 });
        db.LoyaltyCycleSteps.Add(new LoyaltyCycleStep { BranchId = branch.Id, Branch = branch, StepIndex = 1, RewardLabel = "Domicilio fiel gratis", RewardType = LoyaltyRewardType.FreeDelivery, IsActive = true });
        db.DailyPromotions.Add(new DailyPromotion
        {
            BranchId = branch.Id,
            Branch = branch,
            Type = DailyPromotionType.PercentageDiscount,
            DiscountPercentage = 10,
            DiscountScope = DailyPromotionDiscountScope.AllProducts,
            IsActive = true,
            StartsAt = Now.AddHours(-1),
            EndsAt = Now.AddHours(1),
        });
        await db.SaveChangesAsync();
        var (auth, token) = await CreateVerifiedSession(db, customer.Phone1);
        var controller = Controller(db, 720);
        controller.ControllerContext = ContextWithSession(token);

        var request = Request();
        var conflictAction = await controller.Quote(request, default, auth);
        var conflict = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<OkObjectResult>(conflictAction.Result).Value).Data!;
        Assert.True(conflict.BenefitConflict);
        Assert.Equal(2, conflict.AvailableBenefits.Count);
        Assert.Null(conflict.AppliedBenefit);

        request.BenefitSelection = "loyalty";
        var selectedAction = await controller.Quote(request, default, auth);
        var selected = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<OkObjectResult>(selectedAction.Result).Value).Data!;
        Assert.False(selected.BenefitConflict);
        Assert.Equal("loyalty", selected.AppliedBenefit!.Source);
        Assert.Equal(0, selected.Total - selected.Subtotal);
    }

    [Fact]
    public async Task CoveragePreview_ReturnsBranchDistanceTimeAndEstimatedFee()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();

        var action = await Controller(db, 720, 3_000).PreviewCoverage(new PublicCoveragePreviewRequest
        {
            City = "Medellín",
            Address = "Calle 10 # 20-30",
            Latitude = 6.25m,
            Longitude = -75.56m,
        }, default);

        var response = Assert.IsType<ApiResponse<PublicCoveragePreviewDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        var branch = Assert.Single(response.Data!.Branches);
        Assert.True(branch.IsWithinCoverage);
        Assert.Equal(3_000, branch.DistanceMeters);
        Assert.Equal(4_000, branch.EstimatedDeliveryFee);
        Assert.Equal(12, branch.TravelMinutes);
        Assert.Equal("encoded-route", branch.RoutePolyline);
        Assert.Equal(5_000, response.Data.CoverageDistanceMeters);
    }

    [Fact]
    public async Task Quote_SavedAddressPreservesHistoricalDeliveryFee()
    {
        await using var db = CreateDb();
        Seed(db);
        var branch = db.Branches.Local.Single(x => x.Id == 10);
        var customer = new Customer
        {
            Id = 30,
            BranchId = branch.Id,
            Branch = branch,
            Name = "Cliente guardado",
            Phone1 = "3005556677",
            Active = true,
        };
        customer.Addresses.Add(new Address
        {
            Id = 31,
            CustomerId = customer.Id,
            Customer = customer,
            Label = "Casa",
            AddressText = "Calle 10 # 20-30, Medellín",
            DeliveryFee = 9_000,
            IsPrimary = true,
        });
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var (auth, token) = await CreateVerifiedSession(db, customer.Phone1!);
        var controller = Controller(db, 720, 3_000);
        controller.ControllerContext = ContextWithSession(token);
        var request = Request();
        request.Phone = customer.Phone1!;
        request.SavedAddressId = 31;
        request.Address = null;
        request.Latitude = null;
        request.Longitude = null;

        var action = await controller.Quote(request, default, auth);

        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("saved", response.Data!.DeliveryFeeSource);
        Assert.Equal(9_000, response.Data.EstimatedDeliveryFee);
        Assert.Equal(response.Data.Subtotal + 9_000, response.Data.Total);
    }

    [Fact]
    public async Task ConfirmOrder_NewCustomerCreatesSingleIdempotentDeliveryOrderAndAddress()
    {
        await using var db = CreateDb();
        Seed(db);
        var branch = db.Branches.Local.Single(x => x.Id == 10);
        var technicalUser = new User
        {
            Id = 90,
            BranchId = branch.Id,
            Branch = branch,
            Name = "Storefront",
            Email = "storefront@test.local",
            Phone = "3000000000",
            PasswordHash = "not-used",
            Active = true,
        };
        branch.StorefrontTakenByUserId = technicalUser.Id;
        db.Users.Add(technicalUser);
        await db.SaveChangesAsync();
        var (auth, token) = await CreateVerifiedSession(db, "3008889900");
        var controller = Controller(db, 720);
        controller.ControllerContext = ContextWithSession(token);
        var request = new PublicStorefrontOrderRequest
        {
            Name = "Cliente nuevo",
            Phone = "3008889900",
            FulfillmentType = "delivery",
            City = "Medellín",
            Address = "Calle 10 # 20-30",
            AddressAdditionalInfo = "Apartamento 201",
            AddressLabel = "Casa",
            Latitude = 6.25m,
            Longitude = -75.56m,
            SelectedBranchId = branch.Id,
            Items = [new PublicCartItemRequest { ProductId = 20, Quantity = 2 }],
        };
        const string idempotencyKey = "01J6Q5THDVA6PF6P0D7YYS55HA";
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<OrderDto>(It.IsAny<object>())).Returns(new OrderDto());
        var notifications = new Mock<IOrderNotificationService>();
        notifications.Setup(x => x.NotifyNewOrderToKitchen(It.IsAny<OrderDto>())).Returns(Task.CompletedTask);

        var first = await controller.ConfirmOrder(
            request,
            idempotencyKey,
            auth,
            mapper.Object,
            notifications.Object,
            Mock.Of<ILogger<PublicStorefrontController>>(),
            default);
        var second = await controller.ConfirmOrder(
            request,
            idempotencyKey,
            auth,
            mapper.Object,
            notifications.Object,
            Mock.Of<ILogger<PublicStorefrontController>>(),
            default);

        var firstResponse = Assert.IsType<ApiResponse<PublicStorefrontOrderResult>>(Assert.IsType<OkObjectResult>(first.Result).Value);
        var secondResponse = Assert.IsType<ApiResponse<PublicStorefrontOrderResult>>(Assert.IsType<OkObjectResult>(second.Result).Value);
        Assert.Equal(firstResponse.Data!.OrderId, secondResponse.Data!.OrderId);
        var order = Assert.Single(db.Orders.Include(x => x.OrderDetails));
        Assert.Equal("web", order.OrderSource);
        Assert.Equal(OrderStatus.Taken, order.Status);
        Assert.Equal(OrderType.Delivery, order.Type);
        Assert.Equal(100_000, order.Subtotal);
        Assert.Equal(5_000, order.DeliveryFee);
        Assert.Equal(105_000, order.Total);
        Assert.Equal("Cliente nuevo", Assert.Single(db.Customers).Name);
        var address = Assert.Single(db.Addresses);
        Assert.Equal("Casa", address.Label);
        Assert.Equal("Apartamento 201", address.AdditionalInfo);
        Assert.Equal(5_000, address.DeliveryFee);
        notifications.Verify(x => x.NotifyNewOrderToKitchen(It.IsAny<OrderDto>()), Times.Never);
        var notification = Assert.Single(db.PaymentNotificationOutboxMessages);
        Assert.Equal(order.Id, notification.OrderId);
        Assert.Equal("order_created_web_cash", notification.EventType);
    }

    private static PublicDeliveryQuoteRequest Request() => new()
    {
        Name = "Cliente",
        Phone = "3001234567",
        City = "Medellín",
        Address = "Calle 10 # 20-30",
        AddressAdditionalInfo = "Torre A, apartamento 202",
        Latitude = 6.25m,
        Longitude = -75.56m,
        Items = [new() { ProductId = 20, Quantity = 2 }],
    };

    private static PublicStorefrontController Controller(ApplicationDbContext db, int routeSeconds, int routeDistanceMeters = 4_000, string? routePolyline = "encoded-route")
    {
        var routeService = new Mock<IGoogleRoutesDrivingMetricsService>();
        routeService
            .Setup(x => x.ComputeRouteAsync(It.IsAny<IReadOnlyList<(double Latitude, double Longitude)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DrivingRouteMetrics(routeDistanceMeters, routeSeconds, 0, 0, routePolyline));
        var geocoder = new GoogleAddressGeocoder(
            new HttpClient(new GeocodingHandler()),
            Options.Create(new GoogleMapsRouteOptions { GeocodingApiKey = "test" }));
        var configuration = new ConfigurationBuilder().Build();
        var wompi = new Mock<IWompiPaymentService>();
        wompi.Setup(x => x.GetOrderPaymentStatusAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WompiPaymentStatusResult?)null);
        return new PublicStorefrontController(
            db,
            routeService.Object,
            geocoder,
            new FakeClock(Now),
            new BranchBusinessHoursService(db),
            new MemoryCache(Options.Create(new MemoryCacheOptions())),
            new StorefrontQuoteConcurrencyGate(configuration),
            wompi.Object,
            Options.Create(new StorefrontCustomerAuthOptions { TenantId = 1 }));
    }

    private static async Task<(StorefrontCustomerAuthService Service, string Token)> CreateVerifiedSession(
        ApplicationDbContext db,
        string phone)
    {
        string? code = null;
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.SendAuthenticationTemplateMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, string, CancellationToken>((_, _, _, _, _, value, _) => code = value)
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.test", null));
        var service = new StorefrontCustomerAuthService(
            db,
            cloud.Object,
            new FakeClock(Now),
            Options.Create(new StorefrontCustomerAuthOptions
            {
                TenantId = 1,
                AuthenticationBranchId = 10,
                HmacSecret = "storefront-auth-test-secret-32-bytes-minimum",
            }),
            Mock.Of<ILogger<StorefrontCustomerAuthService>>());
        var challenge = await service.RequestCodeAsync(phone, "127.0.0.1", default);
        var verified = await service.VerifyCodeAsync(challenge.ChallengeId, code!, default);
        return (service, verified.SessionToken);
    }

    private static ControllerContext ContextWithSession(string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Storefront-Customer-Session"] = token;
        return new ControllerContext { HttpContext = context };
    }

    private static void Seed(ApplicationDbContext db)
    {
        var branch = new Branch
        {
            Id = 10,
            Name = "Santander",
            Address = "Calle 1",
            Phone1 = "3001234567",
            Latitude = 6.30m,
            Longitude = -75.57m,
            IsActive = true,
        };
        var setting = new WhatsAppBranchSetting
        {
            Id = 11,
            BranchId = branch.Id,
            Branch = branch,
            PhoneNumberId = "phone-id",
            BusinessAccountId = "business-id",
            DisplayPhoneNumber = "+573009999999",
            AccessToken = "secret",
            WebhookVerifyToken = "verify",
            IsActive = true,
            IsVerified = true,
        };
        branch.WhatsAppSetting = setting;
        var category = new ProductCategory { Id = 12, BranchId = branch.Id, Branch = branch, Name = "Arroces", StorefrontRole = "rice" };
        db.AddRange(
            branch,
            setting,
            category,
            new Product { Id = 20, CategoryId = category.Id, Category = category, Name = "Arroz paisa", Price = 50_000, Stock = 10, Active = true });
        db.BranchBusinessHours.AddRange(Enum.GetValues<DayOfWeek>().Select((day, index) => new BranchBusinessHour
        {
            BranchId = branch.Id,
            Branch = branch,
            DayOfWeek = day,
            OpenTime = new TimeOnly(8, 0),
            CloseTime = new TimeOnly(22, 0),
            DisplayOrder = index,
        }));
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class GeocodingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string json = """
                {
                  "status": "OK",
                  "results": [{
                    "formatted_address": "Calle 10 # 20-30, Medellín, Antioquia, Colombia",
                    "types": ["street_address"],
                    "address_components": [
                      {"long_name":"Calle 10","types":["route"]},
                      {"long_name":"20-30","types":["street_number"]},
                      {"long_name":"Medellín","types":["locality"]}
                    ],
                    "geometry": {
                      "location": {"lat": 6.25, "lng": -75.56},
                      "location_type": "ROOFTOP"
                    }
                  }]
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
