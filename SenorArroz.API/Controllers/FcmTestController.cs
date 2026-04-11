using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Notifications.Commands;

namespace SenorArroz.API.Controllers;

/// <summary>Pruebas de push FCM (solo administración).</summary>
[ApiController]
[Route("api/fcm")]
[Authorize(Roles = "Admin,Superadmin")]
public class FcmTestController : ControllerBase
{
    private readonly IMediator _mediator;

    public FcmTestController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Envía una notificación de prueba a domiciliarios libres de la sucursal
    /// (misma regla que pedido listo). Superadmin debe enviar <paramref name="body"/>.BranchId.
    /// </summary>
    [HttpPost("test-free-deliverymen")]
    [ProducesResponseType(typeof(SendTestPushToFreeDeliverymenResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SendTestPushToFreeDeliverymenResultDto>> TestFreeDeliverymen(
        [FromBody] FcmTestFreeDeliverymenRequest? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _mediator.Send(
                new SendTestPushToFreeDeliverymenCommand { BranchId = body?.BranchId },
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public record FcmTestFreeDeliverymenRequest(int? BranchId);
