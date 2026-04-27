using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.Queries;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Models;
using BusinessException = SenorArroz.Domain.Exceptions.BusinessException;

namespace SenorArroz.Tests;

public class GetDashboardExpenseTopLinesHandlerTests
{
    [Fact]
    public async Task Handle_CategoryIdZero_Throws_BusinessException()
    {
        var repo = new Mock<IExpenseDashboardRepository>(MockBehavior.Strict);
        var currentUser = new Mock<ICurrentUser>();
        var handler = new GetDashboardExpenseTopLinesHandler(repo.Object, currentUser.Object);

        await Assert.ThrowsAsync<BusinessException>(async () =>
            await handler.Handle(
                new GetDashboardExpenseTopLinesQuery
                {
                    FromUtc = DateTime.UtcNow,
                    ToUtc = DateTime.UtcNow,
                    CategoryId = 0,
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Calls_Repository_With_Clamped_Limit_600_Becomes_500()
    {
        int? passedTake = null;
        var repo = new Mock<IExpenseDashboardRepository>();
        repo
            .Setup(x => x.GetTopExpenseDetailLinesAsync(
                It.IsAny<int?>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                3,
                It.IsAny<int?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<int?, DateTime, DateTime, int, int?, int, CancellationToken>(
                (_, _, _, _, _, take, _) => passedTake = take)
            .ReturnsAsync(new List<ExpenseTopDetailLineRow>());

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.Role).Returns("Admin");
        currentUser.Setup(x => x.BranchId).Returns(1);

        var handler = new GetDashboardExpenseTopLinesHandler(repo.Object, currentUser.Object);
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await handler.Handle(
            new GetDashboardExpenseTopLinesQuery
            {
                FromUtc = from,
                ToUtc = to,
                CategoryId = 3,
                BranchId = null,
                ExpenseId = null,
                Limit = 600,
            },
            CancellationToken.None);

        Assert.Equal(GetDashboardExpenseTopLinesHandler.MaxLimit, passedTake);
        Assert.Equal(GetDashboardExpenseTopLinesHandler.MaxLimit, result.LimitApplied);
    }
}
