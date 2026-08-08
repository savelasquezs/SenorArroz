using Microsoft.AspNetCore.Mvc.Filters;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.DTOs;

namespace SenorArroz.API.Filters;

/// <summary>
/// Rejects explicit branch values that attempt to escape the resolved request scope.
/// The feature handlers remain responsible for deriving the branch when no explicit
/// value exists and for checking the branch of resources loaded by id.
/// </summary>
public sealed class BranchScopeActionFilter : IAsyncActionFilter
{
    private readonly IBranchContext _branchContext;

    public BranchScopeActionFilter(IBranchContext branchContext)
    {
        _branchContext = branchContext;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            foreach (var (name, value) in context.ActionArguments)
            {
                if (name.Equals("branchId", StringComparison.OrdinalIgnoreCase)
                    && TryGetPositiveBranchId(value, out var routeOrQueryBranchId))
                {
                    _branchContext.ResolveOptional(routeOrQueryBranchId);
                }

                var property = value?.GetType().GetProperty(
                    "BranchId",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.IgnoreCase);

                if (value is UpdateUserDto)
                    continue;

                if (property is not null
                    && TryGetPositiveBranchId(property.GetValue(value), out var payloadBranchId))
                {
                    _branchContext.ResolveOptional(payloadBranchId);
                }
            }
        }

        await next();
    }

    private static bool TryGetPositiveBranchId(object? value, out int branchId)
    {
        branchId = value switch
        {
            int intValue => intValue,
            _ => 0
        };
        return branchId > 0;
    }
}
