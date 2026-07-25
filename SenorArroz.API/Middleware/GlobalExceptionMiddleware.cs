// SenorArroz.API/Middleware/GlobalExceptionMiddleware.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SenorArroz.Application.Common.Interfaces;
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
    private readonly IClock _clock;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment,
        IConfiguration configuration,
        IClock clock)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
        _clock = clock;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurri? una excepci?n no controlada: {Message}", ex.Message);
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
            Timestamp = _clock.UtcNow
        };

        var exposeInternal =
            _environment.IsDevelopment()
            || _configuration.GetValue<bool>("ExposeInternalApiErrors");
        // The exception summary can contain database or infrastructure details,
        // so it must be explicitly enabled instead of being exposed by default.
        var exposeExceptionSummary = _configuration.GetValue("Diagnostics:ExposeExceptionSummary", false);

        switch (exception)
        {
            case BranchContextRequiredException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Code = BranchContextRequiredException.ErrorCode;
                break;

            case BranchScopeMismatchException:
                response.StatusCode = (int)HttpStatusCode.Conflict;
                errorResponse.Code = BranchScopeMismatchException.ErrorCode;
                break;

            case BranchAccessDeniedException:
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                errorResponse.Code = BranchAccessDeniedException.ErrorCode;
                break;

            case SessionReplacedException:
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.Code = SessionReplacedException.ErrorCode;
                break;

            case NotFoundException:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                break;

            case BusinessException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;

            case ValidationException validationEx:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Message = "Errores de validaci?n";
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
                else if (exposeExceptionSummary)
                {
                    var summary = ExceptionSummary(exception);
                    errorResponse.Detail = summary;
                }
                break;
        }

        var jsonResponse = JsonSerializer.Serialize(errorResponse, JsonOptions);

        await response.WriteAsync(jsonResponse);
    }

    /// <summary>Texto acotado (sin pila) para diagnosticar 500 en producción.</summary>
    private static string ExceptionSummary(Exception exception)
    {
        var t = exception.GetType().Name;
        var m = (exception.Message ?? string.Empty).Trim();
        if (m.Length > 500)
            m = m[..500] + "...";
        var inner = exception.InnerException;
        if (inner is not null)
        {
            var im = (inner.Message ?? string.Empty).Trim();
            if (im.Length > 300)
                im = im[..300] + "...";
            if (im.Length > 0)
                m = string.IsNullOrEmpty(m)
                    ? $"{inner.GetType().Name}: {im}"
                    : $"{m} | Inner: {inner.GetType().Name}: {im}";
        }
        return string.IsNullOrEmpty(m) ? t : $"{t}: {m}";
    }

    // Clase interna para la respuesta de error
    private class ErrorResponse
    {
        public bool Success { get; set; }
        public string? Code { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public IDictionary<string, string[]>? Errors { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
