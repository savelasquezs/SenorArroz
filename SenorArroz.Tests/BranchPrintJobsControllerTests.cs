using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class BranchPrintJobsControllerTests
{
    [Fact]
    public async Task Admin_cannot_query_another_branch()
    {
        var queue = new Mock<IPrintQueueService>();
        var controller = CreateController(
            queue.Object,
            new TestCurrentUser(1, "admin", 1));

        var result = await controller.GetStatus(2, 99, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
        queue.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Superadmin_can_query_any_branch()
    {
        var expected = Status(99);
        var queue = new Mock<IPrintQueueService>();
        queue.Setup(x => x.GetJobStatusAsync(2, 99, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = CreateController(
            queue.Object,
            new TestCurrentUser(1, "superadmin", 1));

        var result = await controller.GetStatus(2, 99, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<PrintJobStatusDto>>(ok.Value);
        Assert.Equal(expected, body.Data);
    }

    [Fact]
    public async Task Deliveryman_access_is_checked_with_authenticated_user_id()
    {
        var queue = new Mock<IPrintQueueService>();
        queue.Setup(x => x.GetJobStatusAsync(2, 99, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PrintJobStatusDto?)null);
        var controller = CreateController(
            queue.Object,
            new TestCurrentUser(7, "deliveryman", 1));

        var result = await controller.GetStatus(2, 99, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        queue.Verify(
            x => x.GetJobStatusAsync(2, 99, 7, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static BranchPrintJobsController CreateController(
        IPrintQueueService queue,
        ICurrentUser user) =>
        new(queue, user, NullLogger<BranchPrintJobsController>.Instance);

    private static PrintJobStatusDto Status(long id) =>
        new(
            id,
            "pending",
            "delivery",
            DateTime.UtcNow,
            null,
            null,
            null);

    private sealed class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(int id, string role, int branchId)
        {
            Id = id;
            Role = role;
            BranchId = branchId;
        }

        public int Id { get; }
        public string Role { get; }
        public int BranchId { get; }
        public bool IsAuthenticated => true;
    }
}
