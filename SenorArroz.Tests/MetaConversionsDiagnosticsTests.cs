using System.Text.Json;
using Microsoft.AspNetCore.Http;
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

namespace SenorArroz.Tests;

public sealed class MetaConversionsDiagnosticsTests
{
    [Theory]
    [InlineData("admin", null, 1)]
    [InlineData("superadmin", "2", 1)]
    [InlineData("superadmin", null, 2)]
    public async Task Diagnostics_respect_tenant_and_operational_branch(string role, string? selection, int expected)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.PaymentNotificationOutboxMessages.AddRange(
            Message(1, 1, 1), Message(2, 1, 2), Message(3, 2, 1));
        await db.SaveChangesAsync();
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.Role).Returns(role);
        user.SetupGet(x => x.BranchId).Returns(1);
        var http = new DefaultHttpContext();
        if (selection is not null) http.Request.Headers[BranchContextService.HeaderName] = selection;
        var branch = new BranchContextService(new HttpContextAccessor { HttpContext = http }, user.Object);
        var controller = new MetaConversionsDiagnosticsController(db, branch,
            Options.Create(new MetaConversionsOptions()),
            Options.Create(new StorefrontCustomerAuthOptions { TenantId = 1 }));

        var result = await controller.Status(CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var json = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(expected, json.GetProperty("data").GetProperty("processed").GetInt32());
        if (selection == "2")
            Assert.Equal(2, json.GetProperty("data").GetProperty("latestProcessed").GetProperty("orderId").GetInt32());
        else if (role == "admin")
            Assert.Equal(1, json.GetProperty("data").GetProperty("latestProcessed").GetProperty("orderId").GetInt32());
    }

    private static PaymentNotificationOutboxMessage Message(int id, int tenant, int branch) => new()
    {
        Id = id, TenantId = tenant, BranchId = branch, OrderId = id,
        EventType = "order_created_web_cash", MetaStatus = "processed", MetaProcessedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };
}
