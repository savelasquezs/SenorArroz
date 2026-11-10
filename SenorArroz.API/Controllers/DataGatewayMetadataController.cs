using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.API.Security;

namespace SenorArroz.API.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class DataGatewayMetadataController(IConfiguration config) : ControllerBase
{
    [HttpGet(".well-known/oauth-protected-resource")]
    [HttpGet(".well-known/oauth-protected-resource/api/internal/data-gateway/mcp")]
    public IActionResult Metadata()
    {
        var authority = config["DataGateway:OAuth:Authority"];
        var resource = config["DataGateway:OAuth:Resource"];
        if (!config.GetValue<bool>("DataGateway:Enabled") || string.IsNullOrWhiteSpace(authority)
            || string.IsNullOrWhiteSpace(resource)) return NotFound();
        return Ok(new
        {
            resource, authorization_servers = new[] { authority },
            scopes_supported = new[] { DataGatewaySecurity.Scope },
            bearer_methods_supported = new[] { "header" }
        });
    }
}
