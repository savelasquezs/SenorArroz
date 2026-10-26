using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Inventory.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class InventoryAvailabilityContextTests
{
    [Fact]
    public async Task Order_availability_allows_active_tenant_branch_but_keeps_normal_scope_and_rejects_other_branches()
    {
        var tenant = new TestTenantContext(1);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, currentTenant: tenant, tenantExecutionContext: tenant);
        using (tenant.BeginSystemScope())
        {
            db.Branches.AddRange(new Branch { Id = 2, TenantId = 1, IsActive = true },
                new Branch { Id = 3, TenantId = 2, IsActive = true }, new Branch { Id = 4, TenantId = 1, IsActive = false });
            await db.SaveChangesAsync();
        }
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.Role).Returns("Cashier"); user.SetupGet(x => x.TenantId).Returns(1);
        var branches = new Mock<IBranchContext>();
        branches.Setup(x => x.RequireBranch(2)).Throws(new BranchAccessDeniedException());
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(x => x.GetAvailabilityAsync(It.IsAny<IReadOnlyCollection<int>>(), 2, default, true))
            .ReturnsAsync(Array.Empty<ProductAvailabilityDto>());
        var controller = new InventoryController(inventory.Object, Mock.Of<IInventoryNotificationService>(), branches.Object);
        var allowed = await controller.GetAvailability([1], 2, default, user.Object, db, true);
        Assert.IsType<OkObjectResult>(allowed.Result);
        Assert.IsType<NotFoundResult>((await controller.GetAvailability([1], 3, default, user.Object, db, true)).Result);
        Assert.IsType<NotFoundResult>((await controller.GetAvailability([1], 4, default, user.Object, db, true)).Result);
        await Assert.ThrowsAsync<BranchAccessDeniedException>(() => controller.GetAvailability([1], 2, default, user.Object, db));
        inventory.Verify(x => x.GetAvailabilityAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<int>(), default, true), Times.Once);
    }
}
