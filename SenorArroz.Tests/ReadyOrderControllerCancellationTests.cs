using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Orders.Queries;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class ReadyOrderControllerCancellationTests
{
    private static OrdersController Controller(IMediator mediator, IBranchContext branch) => new(
        mediator, Mock.Of<IClock>(), Mock.Of<IUserRepository>(), Mock.Of<ICurrentUser>(),
        branch, Mock.Of<ICurrentTenant>(), null!);

    [Fact]
    public async Task Endpoint_passes_the_exact_request_token_and_resolved_branch_to_mediator()
    {
        using var cancellation = new CancellationTokenSource();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var branch = new Mock<IBranchContext>(MockBehavior.Strict);
        branch.Setup(b => b.ResolveOptional(99)).Returns(8);
        var expected = new PagedResult<OrderDto> { Items = [], Page = 2, PageSize = 20, TotalCount = 0, TotalPages = 0 };
        mediator.Setup(m => m.Send(It.Is<GetOrdersByStatusQuery>(q =>
                q.Status == OrderStatus.Ready && q.TypeFilter == OrderType.Delivery
                && q.BranchId == 8 && q.Page == 2 && q.PageSize == 20), cancellation.Token))
            .ReturnsAsync(expected);

        var result = await Controller(mediator.Object, branch.Object)
            .GetReadyOrders(99, 2, 20, cancellation.Token);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(result.Result).Value);
        branch.VerifyAll(); mediator.VerifyAll(); mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Aborted_read_propagates_cancellation_without_a_second_send()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var branch = new Mock<IBranchContext>(MockBehavior.Strict);
        branch.Setup(b => b.ResolveOptional(null)).Returns(8);
        mediator.Setup(m => m.Send(It.IsAny<GetOrdersByStatusQuery>(), cancellation.Token))
            .Returns(Task.FromCanceled<PagedResult<OrderDto>>(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Controller(mediator.Object, branch.Object).GetReadyOrders(cancellationToken: cancellation.Token));

        mediator.Verify(m => m.Send(It.IsAny<GetOrdersByStatusQuery>(), cancellation.Token), Times.Once);
    }
}
