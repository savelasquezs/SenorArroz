using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SenorArroz.API.Middleware;
using SenorArroz.Domain.Exceptions;
using System.Text.Json;

namespace SenorArroz.Tests;

/// <summary>
/// Verifica que GlobalExceptionMiddleware devuelve los status codes correctos
/// y serializa el body en camelCase usando el campo estático JsonOptions.
/// </summary>
public class GlobalExceptionMiddlewareTests
{
    private sealed class FakeHostEnvironment(bool isDevelopment = false) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = isDevelopment ? "Development" : "Production";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static GlobalExceptionMiddleware BuildMiddleware(Exception exceptionToThrow, bool isDevelopment = false)
    {
        RequestDelegate next = _ => throw exceptionToThrow;
        var logger = NullLogger<GlobalExceptionMiddleware>.Instance;
        var env = new FakeHostEnvironment(isDevelopment);
        var config = new ConfigurationBuilder().Build();
        var clock = new FakeClock(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        return new GlobalExceptionMiddleware(next, logger, env, config, clock);
    }

    private static async Task<(int StatusCode, JsonDocument Body)> InvokeAsync(
        Exception exceptionToThrow, bool isDevelopment = false)
    {
        var middleware = BuildMiddleware(exceptionToThrow, isDevelopment);

        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        responseBody.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(responseBody).ReadToEndAsync();
        return (context.Response.StatusCode, JsonDocument.Parse(body));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. BusinessException → HTTP 400, body camelCase con mensaje correcto
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task BusinessException_Returns400_WithCamelCaseJson()
    {
        var (status, doc) = await InvokeAsync(new BusinessException("Pedido inválido"));

        Assert.Equal(400, status);

        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("success", out var successProp), "Debe existir 'success' en camelCase");
        Assert.False(successProp.GetBoolean());
        Assert.True(root.TryGetProperty("message", out var messageProp), "Debe existir 'message' en camelCase");
        Assert.Equal("Pedido inválido", messageProp.GetString());

        // Verificar que NO está en PascalCase
        Assert.False(root.TryGetProperty("Success", out _), "No debe existir 'Success' en PascalCase");
        Assert.False(root.TryGetProperty("Message", out _), "No debe existir 'Message' en PascalCase");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. NotFoundException → HTTP 404, body camelCase con mensaje correcto
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task NotFoundException_Returns404_WithCamelCaseJson()
    {
        var (status, doc) = await InvokeAsync(new NotFoundException("Recurso no encontrado"));

        Assert.Equal(404, status);

        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("success", out var successProp));
        Assert.False(successProp.GetBoolean());
        Assert.True(root.TryGetProperty("message", out var messageProp));
        Assert.Equal("Recurso no encontrado", messageProp.GetString());
    }

    [Fact]
    public async Task DeliveryAppUpdateRequired_Returns426_WithPolicyPayload()
    {
        var (status, doc) = await InvokeAsync(
            new DeliveryAppUpdateRequiredException(
                "1.2.5",
                11,
                "https://play.google.com/store/apps/details?id=com.senorarroz.delivery_app"));

        Assert.Equal(StatusCodes.Status426UpgradeRequired, status);
        var root = doc.RootElement;
        Assert.Equal("DELIVERY_APP_UPDATE_REQUIRED", root.GetProperty("code").GetString());
        Assert.Equal("1.2.5", root.GetProperty("requiredVersion").GetString());
        Assert.Equal(11, root.GetProperty("minimumBuild").GetInt32());
        Assert.Contains("play.google.com", root.GetProperty("playStoreUrl").GetString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Excepción genérica en producción → HTTP 500, mensaje genérico, sin detail
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenericException_InProduction_Returns500_WithNoInternalDetail()
    {
        var (status, doc) = await InvokeAsync(
            new Exception("Error interno secreto"),
            isDevelopment: false);

        Assert.Equal(500, status);

        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("message", out var messageProp));
        Assert.Equal("Error interno del servidor", messageProp.GetString());

        // En producción no se expone el detalle interno
        if (root.TryGetProperty("detail", out var detailProp))
            Assert.Equal(JsonValueKind.Null, detailProp.ValueKind);
    }
}
