using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Filters;

public sealed class EmployeeLunchFilter(IApplicationDbContext db, ICurrentUser actor) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = (ControllerActionDescriptor)context.ActionDescriptor;
        var read = HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method);
        var allowed = action.ControllerName == "EmployeeLunch" && action.ActionName is "Me" or "Branch" or "Start" or "Finish"
            || action.ControllerName == "Auth" && action.ActionName is "RefreshToken" or "Logout"
            || action.ControllerName == "Deliveryman" && action.ActionName is "RecordLocation" or "RecordDeviceEvent"
                or "StartWorkSession" or "RegisterDeviceToken" or "RemoveDeviceToken" or "CloseWorkSession";
        if (actor.IsAuthenticated && !read && !allowed
            && !actor.Role.Equals("Kitchen", StringComparison.OrdinalIgnoreCase)
            && await db.EmployeeLunches.AnyAsync(x => x.UserId == actor.Id && x.EndedAt == null, context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new { success = false, code = "EMPLOYEE_ON_LUNCH",
                message = "Finaliza tu almuerzo para continuar trabajando." }) { StatusCode = 409 };
            return;
        }
        var executed = await next();
        for (var error = executed.Exception; error is not null; error = error.InnerException)
        {
            if (error is not PostgresException pg || pg.ConstraintName?.StartsWith("employee_lunch_", StringComparison.Ordinal) != true
                && pg.ConstraintName != "ux_employee_lunch_open") continue;
            executed.ExceptionHandled = true;
            executed.Result = new ObjectResult(new { success = false, code = "EMPLOYEE_LUNCH_CONFLICT",
                message = pg.ConstraintName == "employee_lunch_assignment" ? "El domiciliario está almorzando."
                    : "El almuerzo cambió o el empleado tiene pedidos pendientes. Actualiza el estado." }) { StatusCode = 409 };
            break;
        }
    }
}
