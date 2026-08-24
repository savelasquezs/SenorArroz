using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class PublicStorefrontControllerTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Catalog_ReturnsSharedActiveProductsOnlyOnce()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).GetCatalog(default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<PublicCatalogDto>>(ok.Value);
        var product = Assert.Single(response.Data!.Products);
        Assert.Equal(50_000, product.Price);
        Assert.Equal("Arroces", product.CategoryName);
    }

    [Fact]
    public async Task Quote_UsesTravelOnlyForCoverage_AndAddsPreparationToEta()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();

        var action = await Controller(db, 1800).Quote(Request(), default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(ok.Value);
        Assert.False(response.Data!.IsOutsideCoverage);
        Assert.Equal(30, response.Data.TravelMinutes);
        Assert.Equal(20, response.Data.PreparationMinutes);
        Assert.Equal(50, response.Data.EstimatedTotalMinutes);
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
        Assert.Equal(51, response.Data.EstimatedTotalMinutes);
        Assert.Contains("SOLICITUD%20FUERA%20DE%20COBERTURA", response.Data.WhatsAppUrl);
    }

    private static PublicDeliveryQuoteRequest Request() => new()
    {
        Name = "Cliente",
        Phone = "3001234567",
        City = "Medellín",
        Address = "Calle 10 # 20-30",
        Latitude = 6.25m,
        Longitude = -75.56m,
        Items = [new() { ProductId = 20, Quantity = 2 }],
    };

    private static PublicStorefrontController Controller(ApplicationDbContext db, int routeSeconds)
    {
        var routeService = new Mock<IGoogleRoutesDrivingMetricsService>();
        routeService
            .Setup(x => x.ComputeRouteAsync(It.IsAny<IReadOnlyList<(double Latitude, double Longitude)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DrivingRouteMetrics(8_000, routeSeconds, 0, 0));
        var geocoder = new GoogleAddressGeocoder(
            new HttpClient(new GeocodingHandler()),
            Options.Create(new GoogleMapsRouteOptions { GeocodingApiKey = "test" }));
        return new PublicStorefrontController(db, routeService.Object, geocoder, new FakeClock(Now));
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
            DisplayPhoneNumber = "+573001234567",
            AccessToken = "secret",
            WebhookVerifyToken = "verify",
            IsActive = true,
            IsVerified = true,
        };
        branch.WhatsAppSetting = setting;
        var category = new ProductCategory { Id = 12, BranchId = branch.Id, Branch = branch, Name = "Arroces" };
        db.AddRange(
            branch,
            setting,
            category,
            new Product { Id = 20, CategoryId = category.Id, Category = category, Name = "Arroz paisa", Price = 50_000, Stock = 10, Active = true });
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

