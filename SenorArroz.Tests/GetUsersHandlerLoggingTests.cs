using AutoMapper;
using Microsoft.Extensions.Logging;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Application.Features.Users.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Tests;

public sealed class GetUsersHandlerLoggingTests
{
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    public async Task Handle_LogsOnlyAggregateAtDebugAndPreservesFilteredPagination(bool active, int expectedCount)
    {
        var repository = new Mock<IUserRepository>();
        var mapper = new Mock<IMapper>();
        var current = new Mock<ICurrentUser>();
        var logger = new Mock<ILogger<GetUsersHandler>>();
        current.SetupGet(x => x.Role).Returns(Roles.Admin);
        current.SetupGet(x => x.BranchId).Returns(1);

        var users = new List<User>
        {
            new() { Id = 1, Name = "Synthetic Alpha", BranchId = 1, Role = UserRole.Deliveryman, Active = true },
            new() { Id = 2, Name = "Synthetic Beta", BranchId = 1, Role = UserRole.Cashier, DeliveryAppEnabled = true, Active = true },
            new() { Id = 3, Name = "Synthetic Gamma", BranchId = 1, Role = UserRole.Cashier, Active = true }
        };
        repository.Setup(x => x.GetAllAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((IEnumerable<User>)users);
        mapper.Setup(x => x.Map<List<UserDto>>(It.IsAny<object>()))
            .Returns((object source) => ((IEnumerable<User>)source).Select(u => new UserDto { Id = u.Id, Name = u.Name }).ToList());

        var handler = new GetUsersHandler(repository.Object, mapper.Object, current.Object, new TestBranchContext(1), logger.Object);
        var result = await handler.Handle(new GetUsersQuery(Role: "Deliveryman", Active: active, Page: 2, PageSize: 1), CancellationToken.None);

        Assert.Equal(expectedCount, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(expectedCount, result.TotalPages);
        if (active)
            Assert.Equal(2, Assert.Single(result.Items).Id);
        else
            Assert.Empty(result.Items);

        repository.Verify(x => x.GetAllAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        var log = Assert.Single(logger.Invocations.Where(x => x.Method.Name == nameof(ILogger.Log)));
        Assert.Equal(LogLevel.Debug, (LogLevel)log.Arguments[0]);
        var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(log.Arguments[2]);
        Assert.Equal(expectedCount, Assert.IsType<int>(Assert.Single(state.Where(x => x.Key == "Count")).Value));
        Assert.Equal(2, state.Count);
        Assert.DoesNotContain("Synthetic", log.Arguments[2].ToString()!);
    }
}
