using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.Commands;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Application.Features.Users.Queries;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public sealed class UsersControllerTests
{
    [Fact]
    public async Task UpdateUser_AllowsSuperadminToMoveUserOutOfSelectedBranch()
    {
        var mediator = new Mock<IMediator>();
        var currentUser = new Mock<ICurrentUser>();
        var branchContext = new TestBranchContext(branchId: 1);
        var dto = new UpdateUserDto
        {
            Name = "Andres Felipe Restrepo",
            Email = "andres@example.com",
            Phone = "3045613634",
            Role = UserRole.Deliveryman,
            Active = true,
            BranchId = 2
        };

        currentUser.SetupGet(x => x.Role).Returns(Roles.Superadmin);
        mediator
            .Setup(x => x.Send(It.Is<GetUserByIdQuery>(q => q.Id == 10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDto { Id = 10, BranchId = 1 });
        mediator
            .Setup(x => x.Send(It.Is<UpdateUserCommand>(c => c.UserId == 10 && c.UserData == dto), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDto { Id = 10, BranchId = 2 });

        var controller = new UsersController(mediator.Object, branchContext, currentUser.Object);

        var result = await controller.UpdateUser(10, dto);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.IsType<UserDto>(ok.Value);
        Assert.Equal(2, user.BranchId);
    }
}
