using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class WompiWebhooksController(IWompiPaymentService wompi, ILogger<WompiWebhooksController> logger) : ControllerBase
{
    [HttpPost("api/integrations/wompi/webhooks/{environment}")]
    [EnableRateLimiting("rappi-webhook")]
    [RequestSizeLimit(128 * 1024)]
    public async Task<IActionResult> Webhook(string environment, CancellationToken cancellationToken)
    {
        if (environment is not "sandbox" and not "production") return NotFound();
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            var result = await wompi.ProcessWebhookAsync(environment, raw, Request.Headers["X-Event-Checksum"].FirstOrDefault(), cancellationToken);
            if (!result.Accepted)
            {
                logger.LogWarning("Webhook Wompi rechazado en {Environment}: {Error}", environment, result.Error);
                return BadRequest();
            }
            return Ok();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error procesando webhook Wompi en {Environment}.", environment);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
