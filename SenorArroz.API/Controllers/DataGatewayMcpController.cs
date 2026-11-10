using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SenorArroz.API.Security;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Controllers;

/// <summary>
/// Sessionless MCP Streamable HTTP JSON-RPC endpoint over the existing HTTPS API.
/// Uses the shared gateway authorization policy for local JWT or OAuth access.
/// </summary>
[ApiController]
[Route("api/internal/data-gateway/mcp")]
[Authorize(Policy = DataGatewaySecurity.Policy)]
[EnableRateLimiting(DataGatewaySecurity.Policy)]
[RequestSizeLimit(32768)]
public sealed class DataGatewayMcpController(
    IConfiguration config,
    ICurrentTenant tenant,
    ILogger<DataGatewayController> gatewayLogger) : ControllerBase
{
    private const string Version = "2025-06-18";
    private static readonly string[] Versions = ["2025-03-26", Version, "2025-11-25"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly object[] Tools =
    [
        new {
            name = "get_schema",
            description = "List explicitly allowed diagnostic views and columns",
            inputSchema = new { type = "object", properties = new { }, additionalProperties = false },
            annotations = new { readOnlyHint = true, destructiveHint = false, idempotentHint = true, openWorldHint = false }
        },
        new {
            name = "execute_query",
            description = "Read a permitted diagnostic view with filters; no arbitrary SQL",
            inputSchema = QuerySchema(),
            annotations = new { readOnlyHint = true, destructiveHint = false, idempotentHint = true, openWorldHint = false }
        },
        new {
            name = "explain_query",
            description = "EXPLAIN without ANALYZE for a structured query",
            inputSchema = QuerySchema(),
            annotations = new { readOnlyHint = true, destructiveHint = false, idempotentHint = true, openWorldHint = false }
        }
    ];

    private static object QuerySchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["view"] = new { type = "string" },
            ["columns"] = new { type = "array", maxItems = 30, uniqueItems = true, items = new { type = "string", maxLength = 63 } },
            ["filters"] = new
            {
                type = "array",
                maxItems = 20,
                items = new
                {
                    type = "object",
                    required = new[] { "column", "op", "value" },
                    additionalProperties = false,
                    properties = new
                    {
                        column = new { type = "string" },
                        op = new { type = "string", @enum = new[] { "eq", "ne", "gt", "gte", "lt", "lte" } },
                        value = new { type = new[] { "string", "number", "boolean", "null" } }
                    }
                }
            },
            ["orderBy"] = new { type = "string" },
            ["descending"] = new { type = "boolean" },
            ["maxRows"] = new { type = "integer", minimum = 1, maximum = 500 }
        },
        required = new[] { "view" },
        additionalProperties = false
    };

    private object Rpc(object? id, object result) => new { jsonrpc = "2.0", id, result };
    private object Error(object? id, int code, string message) =>
        new { jsonrpc = "2.0", id, error = new { code, message } };

    [HttpGet]
    [HttpDelete]
    public IActionResult NoStream() => StatusCode(405);

    [HttpPost]
    public async Task<IActionResult> Handle([FromBody] JsonElement request, CancellationToken ct)
    {
        if (!config.GetValue<bool>("DataGateway:Enabled")
            || !tenant.HasTenant || tenant.TenantId != config.GetValue("DataGateway:ReaderTenantId", 1)
            || config.GetSection("DataGateway:AllowedViews").Get<string[]>() is not { Length: > 0 }
            || string.IsNullOrWhiteSpace(config.GetConnectionString("DataGatewayReader")))
            return NotFound();

        if (Request.Headers.TryGetValue("Origin", out var origin)
            && !(config.GetSection("DataGateway:AllowedOrigins").Get<string[]>() ?? [])
                .Contains(origin.ToString(), StringComparer.Ordinal))
            return StatusCode(403);
        if (Request.Headers.TryGetValue("MCP-Protocol-Version", out var protocol)
            && !Versions.Contains(protocol.ToString(), StringComparer.Ordinal))
            return BadRequest(Error(null, -32600, "Unsupported MCP protocol version"));

        if (request.ValueKind != JsonValueKind.Object
            || !request.TryGetProperty("jsonrpc", out var version)
            || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0"
            || !request.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String)
            return Ok(Error(null, -32600, "Invalid JSON-RPC request"));

        var method = methodElement.GetString();
        if (!request.TryGetProperty("id", out var idElement))
        {
            // MCP client initialization notifications have no response.
            return Accepted();
        }
        if (idElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            return BadRequest(Error(null, -32600, "Invalid request ID"));
        object id = idElement.Clone();

        switch (method)
        {
            case "initialize":
                var selectedVersion = Version;
                if (request.TryGetProperty("params", out var init) && init.ValueKind == JsonValueKind.Object
                    && init.TryGetProperty("protocolVersion", out var requested)
                    && requested.ValueKind == JsonValueKind.String && Versions.Contains(requested.GetString()!))
                    selectedVersion = requested.GetString()!;
                return Ok(Rpc(id, new
                {
                    protocolVersion = selectedVersion,
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "senor-arroz-data-gateway", version = "1.0.0" }
                }));
            case "ping":
                return Ok(Rpc(id, new { }));
            case "tools/list":
                return Ok(Rpc(id, new { tools = Tools }));
            case "tools/call":
                if (!request.TryGetProperty("params", out var call) || call.ValueKind != JsonValueKind.Object
                    || !call.TryGetProperty("name", out var toolName) || toolName.ValueKind != JsonValueKind.String)
                    return Ok(Error(id, -32602, "Invalid tool call"));
                return Ok(Rpc(id, await Call(request, ct)));
            default:
                return Ok(Error(id, -32601, "Method not found"));
        }
    }

    private async Task<object> Call(JsonElement request, CancellationToken ct)
    {
        if (!request.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
            return ToolError("Invalid tool call");

        var name = nameElement.GetString();
        if (name is not ("get_schema" or "execute_query" or "explain_query"))
            return ToolError("Unknown tool");

        JsonElement arguments = default;
        if (parameters.TryGetProperty("arguments", out var args))
            arguments = args;

        var controller = new DataGatewayController(config, tenant, gatewayLogger)
        {
            ControllerContext = ControllerContext
        };
        IActionResult result;
        try
        {
            if (name == "get_schema")
            {
                if (arguments.ValueKind != JsonValueKind.Undefined
                    && (arguments.ValueKind != JsonValueKind.Object || arguments.EnumerateObject().Any()))
                    return ToolError("Invalid arguments");
                result = await controller.Schema(ct);
            }
            else
            {
                if (arguments.ValueKind != JsonValueKind.Object)
                    return ToolError("Invalid arguments");
                var query = arguments.Deserialize<DataGatewayController.QueryRequest>(JsonOptions);
                if (query is null) return ToolError("Invalid arguments");
                result = name == "execute_query"
                    ? await controller.Query(query, ct)
                    : await controller.Explain(query, ct);
            }
        }
        catch (JsonException)
        {
            return ToolError("Invalid arguments");
        }
        if (result is OkObjectResult ok)
            return new
            {
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(ok.Value) } },
                structuredContent = ok.Value,
                isError = false
            };
        return ToolError(result is ObjectResult obj
            ? "Query rejected (" + obj.StatusCode + ")"
            : "Gateway unavailable");
    }

    private static object ToolError(string message) => new
    {
        content = new[] { new { type = "text", text = message } },
        isError = true
    };
}
