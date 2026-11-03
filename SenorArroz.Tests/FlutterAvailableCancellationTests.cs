using System.Reflection;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Tests;

public sealed class FlutterAvailableCancellationTests
{
    [Fact]
    public void Delivery_available_endpoint_accepts_request_cancellation_and_keeps_deliveryman_role()
    {
        var method = typeof(OrdersController).GetMethod(
            nameof(OrdersController.GetAvailableOrdersForDelivery))!;
        var token = Assert.Single(method.GetParameters(),
            parameter => parameter.ParameterType == typeof(CancellationToken));
        Assert.Equal("cancellationToken", token.Name);
        var authorization = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Deliveryman", authorization?.Roles);
    }

    [Fact]
    public async Task Delivery_available_handler_forwards_token_and_enforces_assigned_branch()
    {
        using var cts = new CancellationTokenSource();
        var repository = new Mock<IOrderRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetAvailableOrdersForDeliveryAsync(8, cts.Token))
            .ReturnsAsync(new List<Order>());
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<List<OrderDto>>(It.IsAny<object>()))
            .Returns(new List<OrderDto>());
        var current = new Mock<ICurrentUser>();
        current.SetupGet(u => u.Role).Returns("Deliveryman");
        current.SetupGet(u => u.BranchId).Returns(8);

        var handler = new GetAvailableOrdersForDeliveryHandler(
            repository.Object, mapper.Object, current.Object);
        var result = await handler.Handle(new GetAvailableOrdersForDeliveryQuery
        {
            BranchId = 8
        }, cts.Token);

        Assert.Empty(result);
        repository.Verify(r => r.GetAvailableOrdersForDeliveryAsync(8, cts.Token),
            Times.Once);
    }
}
