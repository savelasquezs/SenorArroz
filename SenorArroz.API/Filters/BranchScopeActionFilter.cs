using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.API.Filters;

/// <summary>
/// Rejects explicit branch values that attempt to escape the resolved request scope.
/// The feature handlers remain responsible for deriving the branch when no explicit
/// value exists and for checking the branch of resources loaded by id.
/// </summary>
public sealed class BranchScopeActionFilter : IAsyncActionFilter
{
    private readonly IBranchContext _branchContext;
    private readonly IApplicationDbContext? _context;

    public BranchScopeActionFilter(IBranchContext branchContext, IApplicationDbContext? context = null)
    {
        _branchContext = branchContext;
        _context = context;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            if (_context is not null && _branchContext.SelectedBranchId is int selectedBranchId)
                await EnsureTenantBranchAsync(selectedBranchId, context.HttpContext.RequestAborted);

            foreach (var (name, value) in context.ActionArguments)
            {
                if (name.Equals("branchId", StringComparison.OrdinalIgnoreCase)
                    && TryGetPositiveBranchId(value, out var routeOrQueryBranchId))
                {
                    _branchContext.ResolveOptional(routeOrQueryBranchId);
                    await EnsureTenantBranchAsync(routeOrQueryBranchId, context.HttpContext.RequestAborted);
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
                    await EnsureTenantBranchAsync(payloadBranchId, context.HttpContext.RequestAborted);
                }
            }
        }

        await next();
    }

    private async Task EnsureTenantBranchAsync(int branchId, CancellationToken cancellationToken)
    {
        if (_context is not null && !await _context.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            throw new BranchAccessDeniedException();
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
