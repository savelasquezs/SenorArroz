using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using SenorArroz.API.Filters;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public sealed class BranchScopeActionFilterTests
{
    [Fact]
    public async Task OnActionExecutionAsync_DoesNotValidateUpdateUserDtoBranchIdAgainstActiveBranch()
    {
        var branchContext = new Mock<IBranchContext>(MockBehavior.Strict);
        var filter = new BranchScopeActionFilter(branchContext.Object);
        var dto = new UpdateUserDto
        {
            Name = "Pipe",
            Email = "pipe@example.com",
            Phone = "3146777140",
            Role = UserRole.Deliveryman,
            Active = true,
            BranchId = 2
        };

        var actionContext = CreateActionContext(new Dictionary<string, object?>
        {
            ["updateUserDto"] = dto
        });

        var executed = false;
        await filter.OnActionExecutionAsync(actionContext, () =>
        {
            executed = true;
            return Task.FromResult(new ActionExecutedContext(
                actionContext,
                new List<IFilterMetadata>(),
                new EmptyController()));
        });

        Assert.True(executed);
        branchContext.Verify(x => x.ResolveOptional(It.IsAny<int?>()), Times.Never);
    }

    private static ActionExecutingContext CreateActionContext(Dictionary<string, object?> arguments)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("sub", "1") },
            "Test"));

        return new ActionExecutingContext(
            new ActionContext(
                httpContext,
                new RouteData(),
                new ControllerActionDescriptor()),
            new List<IFilterMetadata>(),
            arguments,
            new EmptyController());
    }

    private sealed class EmptyController : Controller
    {
    }
}
