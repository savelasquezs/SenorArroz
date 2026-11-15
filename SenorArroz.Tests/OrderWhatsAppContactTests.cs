using System.Text.Json;
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Mappings;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public sealed class OrderWhatsAppContactTests
{
    [Theory]
    [InlineData(null, "@cliente_arroz")]
    [InlineData("3001234567", "@cliente_arroz")]
    [InlineData("3001234567", null)]
    [InlineData(null, null)]
    public async Task ContactSurvivesDatabaseListDetailsAndRouteContext(string? phone, string? username)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        db.Branches.Add(new Branch { Id = 7, Name = "Santander", Address = "Sucursal" });
        db.Users.Add(new User { Id = 2, BranchId = 7, Role = UserRole.Deliveryman,
            Active = true, Name = "Courier", Email = "courier@example.invalid" });
        db.Customers.Add(new Customer { Id = 3, BranchId = 7, Name = "Cliente",
            Phone1 = phone, WhatsAppUsername = username });
        db.DeliveryRoutes.Add(new DeliveryRoute { Id = 20, BranchId = 7, DeliverymanId = 2,
            Status = DeliveryRouteStatus.InProgress, LastAssignmentAtUtc = DateTime.UtcNow });
        db.Orders.Add(new Order { Id = 30, TakenById = 2, BranchId = 7, CustomerId = 3,
            DeliveryManId = 2, DeliveryRouteId = 20, Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay, StatusTimes = "{}", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var mapper = new MapperConfiguration(config => config.AddProfile<OrderMappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();
        var repository = new OrderRepository(db, new SystemUtcClock());
        var list = await repository.SearchOrdersAsync(branchId: 7, page: 1, pageSize: 10);
        AssertContact(mapper.Map<OrderDto>(Assert.Single(list.Items)));
        AssertContact(mapper.Map<OrderWithDetailsDto>(await repository.GetByIdWithFullDetailsAsync(30)));

        var actor = new Mock<ICurrentUser>();
        actor.SetupGet(x => x.Id).Returns(2);
        actor.SetupGet(x => x.TenantId).Returns(1);
        actor.SetupGet(x => x.BranchId).Returns(7);
        var service = new DeliveryOperationsService(db, actor.Object, new SystemUtcClock(),
            Mock.Of<ISender>(), mapper, new InlineRouteLock());
        AssertContact(Assert.Single((await service.ContextAsync(default)).Orders));

        var guest = mapper.Map<OrderWithDetailsDto>(new Order { StatusTimes = "{}" });
        Assert.Null(guest.CustomerWhatsAppUsername);
        Assert.Null(guest.CustomerPhone);

        void AssertContact(OrderDto dto)
        {
            Assert.Equal(phone, dto.CustomerPhone);
            Assert.Equal(username, dto.CustomerWhatsAppUsername);
            var json = JsonSerializer.SerializeToElement(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(username, json.GetProperty("customerWhatsAppUsername").GetString());
            Assert.False(json.TryGetProperty("whatsAppUserId", out _));
        }
    }

    private sealed class InlineRouteLock : IDeliveryAutoCompletionRouteLock
    {
        public Task ExecuteAsync(int routeId, Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default) => action(cancellationToken);
    }
}
