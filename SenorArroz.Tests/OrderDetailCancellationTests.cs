using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class OrderDetailCancellationTests
{
    [Fact]
    public async Task Detail_endpoints_forward_the_request_cancellation_token_to_MediatR()
    {
        using var cts = new CancellationTokenSource();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator.Setup(m => m.Send(It.Is<GetOrderByIdQuery>(q => q.Id == 41), cts.Token))
            .ReturnsAsync((OrderDto?)null);
        mediator.Setup(m => m.Send(It.Is<GetOrderWithDetailsQuery>(q => q.Id == 42), cts.Token))
            .ReturnsAsync((OrderWithDetailsDto?)null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var controller = new OrdersController(mediator.Object, Mock.Of<IClock>(),
            Mock.Of<IUserRepository>(), Mock.Of<ICurrentUser>(), Mock.Of<IBranchContext>(),
            Mock.Of<ICurrentTenant>(), db);

        Assert.IsType<NotFoundResult>((await controller.GetOrder(41, cts.Token)).Result);
        Assert.IsType<NotFoundResult>((await controller.GetOrderWithDetails(42, cts.Token)).Result);
        mediator.VerifyAll();
    }
}
