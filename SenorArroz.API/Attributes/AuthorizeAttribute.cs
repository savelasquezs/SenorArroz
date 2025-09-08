using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SenorArroz.Domain.Entities;



namespace SenorArroz.API.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[]? _roles;

    public AuthorizeAttribute(params string[] roles)
    {
        _roles = roles;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Skip authorization if action is decorated with [AllowAnonymous] attribute
        var allowAnonymous = context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any();
        if (allowAnonymous)
            return;

        // Authorization
        var user = (User?)context.HttpContext.Items["User"];
        if (user == null)
        {
            // Not logged in or token invalid
            context.Result = new JsonResult(new { message = "Unauthorized" })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        // Role-based authorization
        if (_roles != null && _roles.Length > 0 && !_roles.Contains(user.Role?.ToString() ?? string.Empty))
        {
            // User's role is not authorized
            context.Result = new JsonResult(new { message = "Forbidden" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class AllowAnonymousAttribute : Attribute
{
}