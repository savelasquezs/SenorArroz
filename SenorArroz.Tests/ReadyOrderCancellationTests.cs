using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class ReadyOrderCancellationTests
{
    [Fact]
    public void Ready_endpoint_accepts_the_http_request_cancellation_token()
    {
        var method = typeof(OrdersController).GetMethod(nameof(OrdersController.GetReadyOrders))!;
        var token = Assert.Single(method.GetParameters(),
            parameter => parameter.ParameterType == typeof(CancellationToken));
        Assert.Equal("cancellationToken", token.Name);
        Assert.Equal(typeof(CancellationToken), token.ParameterType);
    }

    [Fact]
    public async Task Ready_handler_passes_request_token_to_repository_and_preserves_branch_scope()
    {
        using var cts = new CancellationTokenSource();
        var repository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var rows = new PagedResult<Order>
        {
            Items = new List<Order>(),
            TotalCount = 0,
            Page = 1,
            PageSize = 100,
            TotalPages = 0
        };
        repository.Setup(r => r.GetByStatusAsync(
                OrderStatus.Ready, OrderType.Delivery,
                8, 1, 100, null, "asc", cts.Token))
            .ReturnsAsync(rows);

        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<List<OrderDto>>(It.IsAny<object>()))
            .Returns(new List<OrderDto>());
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.Role).Returns("Deliveryman");
        current.SetupGet(x => x.BranchId).Returns(8);
        var handler = new GetOrdersByStatusHandler(repository.Object, mapper.Object, current.Object);

        var result = await handler.Handle(new GetOrdersByStatusQuery
        {
            Status = OrderStatus.Ready,
            TypeFilter = OrderType.Delivery,
            BranchId = 99, // A deliveryman cannot widen the authorized branch.
            Page = 1,
            PageSize = 100
        }, cts.Token);

        Assert.Empty(result.Items);
        repository.VerifyAll();
    }
}
