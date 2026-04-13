// SenorArroz.API/Middleware/GlobalExceptionMiddleware.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SenorArroz.Domain.Exceptions;
using System.Net;
using System.Text.Json;

namespace SenorArroz.API.Middleware;

public class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurriù una excepciùn no controlada: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            Success = false,
            Message = exception.Message,
            Timestamp = DateTime.UtcNow
        };

        var exposeInternal =
            _environment.IsDevelopment()
            || _configuration.GetValue<bool>("ExposeInternalApiErrors");

        switch (exception)
        {
            case NotFoundException:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                break;

            case BusinessException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;

            case ValidationException validationEx:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Message = "Errores de validaciùn";
                errorResponse.Errors = validationEx.Errors;
                break;

            case UnauthorizedAccessException:
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.Message = "No autorizado";
                break;

            default:
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                errorResponse.Message = "Error interno del servidor";
                if (exposeInternal)
                {
                    errorResponse.Detail = exception.ToString();
                }
                break;
        }

        var jsonResponse = JsonSerializer.Serialize(errorResponse, JsonOptions);

        await response.WriteAsync(jsonResponse);
    }

    // Clase interna para la respuesta de error
    private class ErrorResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public IDictionary<string, string[]>? Errors { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
