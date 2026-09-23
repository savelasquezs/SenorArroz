using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class GetNeighborhoodsForOrderAddressTests
{
    [Fact]
    public async Task Cashier_CanLoadOnlyActiveNeighborhoodsFromAnotherActiveBranch()
    {
        var neighborhoods = new Mock<INeighborhoodRepository>();
        neighborhoods.Setup(x => x.GetByBranchIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Neighborhood { Id = 10, BranchId = 2, Active = true },
                new Neighborhood { Id = 11, BranchId = 2, Active = false }
            ]);
        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = 2, IsActive = true });
        var handler = new GetNeighborhoodsHandler(neighborhoods.Object, branches.Object, new CurrentUser(Roles.Cashier, 1));

        var result = await handler.Handle(new GetNeighborhoodsQuery { BranchId = 2, ForOrderAddress = true }, default);

        Assert.Equal(10, Assert.Single(result).Id);
    }

    [Fact]
    public async Task CrossBranchLookup_RejectsInactiveOrUnavailableTenantBranch()
    {
        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Branch?)null);
        var handler = new GetNeighborhoodsHandler(
            Mock.Of<INeighborhoodRepository>(),
            branches.Object,
            new CurrentUser(Roles.Admin, 1));

        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new GetNeighborhoodsQuery { BranchId = 2, ForOrderAddress = true },
            default));
    }

    [Fact]
    public async Task CrossBranchLookup_RejectsRolesOutsidePosOrderCreation()
    {
        var handler = new GetNeighborhoodsHandler(
            Mock.Of<INeighborhoodRepository>(),
            Mock.Of<IBranchRepository>(),
            new CurrentUser(Roles.Deliveryman, 1));

        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new GetNeighborhoodsQuery { BranchId = 2, ForOrderAddress = true },
            default));
    }

    private sealed record CurrentUser(string Role, int BranchId) : ICurrentUser
    {
        public int Id => 1;
        public bool IsAuthenticated => true;
    }
}
