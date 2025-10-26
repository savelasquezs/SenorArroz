using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace SenorArroz.API.Hubs;

[Authorize]
public class OrderHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        
        if (!string.IsNullOrEmpty(branchId))
        {
            // Agregar usuario a grupo de su sucursal
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Branch_{branchId}");
            
            // Agregar a grupo específico de rol
            if (role == "Kitchen")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Branch_{branchId}_Kitchen");
            }
            else if (role == "Deliveryman")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Branch_{branchId}_Delivery");
            }
        }
        
        await base.OnConnectedAsync();
    }
    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        
        if (!string.IsNullOrEmpty(branchId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Branch_{branchId}");
            
            if (role == "Kitchen")
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Branch_{branchId}_Kitchen");
            }
            else if (role == "Deliveryman")
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Branch_{branchId}_Delivery");
            }
        }
        
        await base.OnDisconnectedAsync(exception);
    }
}

