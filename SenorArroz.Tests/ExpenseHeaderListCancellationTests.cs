using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Application.Features.ExpenseHeaders.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class ExpenseHeaderListCancellationTests
{
    [Fact]
    public async Task Listing_forwards_request_abort_without_changing_filters_or_permissions()
    {
        using var cts = new CancellationTokenSource();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator.Setup(m => m.Send(
            It.Is<GetExpenseHeadersQuery>(query =>
                query.Page == 2 && query.PageSize == 25 &&
                query.BranchId == 9 && query.ExpenseName == "arroz" &&
                query.CategoryNames.Count == 1 && query.CategoryNames[0] == "Operación" &&
                query.SortBy == "total" && query.SortOrder == "desc"),
            cts.Token))
            .ReturnsAsync(new PagedResult<ExpenseHeaderDto>
            {
                Items = new List<ExpenseHeaderDto>(),
                Page = 2, PageSize = 25, TotalCount = 0, TotalPages = 0,
            });

        var controller = new ExpenseHeadersController(mediator.Object);
        var result = await controller.GetExpenseHeaders(
            page: 2, pageSize: 25, sortBy: "total", sortOrder: "desc",
            branchId: 9, categoryNames: new List<string> { "Operación" },
            expenseName: "arroz", cancellationToken: cts.Token);

        Assert.IsType<OkObjectResult>(result.Result);
        mediator.VerifyAll();
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_even_when_already_canceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator.Setup(m => m.Send(It.IsAny<GetExpenseHeadersQuery>(), cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var controller = new ExpenseHeadersController(mediator.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.GetExpenseHeaders(cancellationToken: cts.Token));
        mediator.VerifyAll();
    }
}
