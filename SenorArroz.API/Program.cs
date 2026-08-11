// SenorArroz.API/Program.cs - Updated with Authentication
using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SenorArroz.API.Extensions;
using SenorArroz.API.Hosting;
using SenorArroz.API.Middleware;
using SenorArroz.API.Filters;
using SenorArroz.Application;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure;
using SenorArroz.Infrastructure.Storage;
using System.Text.Json;
using System.Text.Json.Serialization;

GoogleCredentialBootstrap.ApplyFromEnvironment();

var builder = WebApplication.CreateBuilder(args);

GoogleCredentialBootstrap.ApplyFromConfiguration(builder.Configuration);

// Add services to the container
builder.Services.AddScoped<BranchScopeActionFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<BranchScopeActionFilter>();
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(new SnakeCaseNamingPolicy())
    );
}); ;
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    var defaultFactory = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
    {
        if (context.HttpContext.Request.Path.StartsWithSegments("/api/deliverymen/location"))
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("DeliveryLocationValidation");
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                    $"{entry.Key}: {error.ErrorMessage}"))
                .ToArray();

            logger.LogWarning(
                "DELIVERY_LOCATION_REJECTED userId={UserId} branchId={BranchId} traceId={TraceId} errors={Errors}",
                context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                context.HttpContext.User.FindFirst("branch_id")?.Value,
                context.HttpContext.TraceIdentifier,
                string.Join(" | ", errors));
        }

        return defaultFactory(context);
    };
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SenorArroz API",
        Version = "v1",
        Description = "API for SeñorArroz restaurant management system"
    });

    // Add JWT Authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header
            },
            new List<string>()
        }
    });

    // Include XML comments
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Add Application and Infrastructure services
builder.Services.AddApplication();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.Configure<DeliveryAppVersionOptions>(
    builder.Configuration.GetSection(DeliveryAppVersionOptions.SectionName));

builder.Services.AddScoped<IBranchReceiptLogoStorage>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<FirebaseStorageOptions>>().Value;
    if (opts.Enabled && !string.IsNullOrWhiteSpace(opts.Bucket))
        return new BranchReceiptLogoGcsStorage(
            sp.GetRequiredService<IFirebaseGcsStorage>(),
            sp.GetRequiredService<IOptions<FirebaseStorageOptions>>());
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    return new BranchReceiptLogoStorage(root);
});

builder.Services.AddScoped<IUserProfileImageStorage>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<FirebaseStorageOptions>>().Value;
    if (opts.Enabled && !string.IsNullOrWhiteSpace(opts.Bucket))
        return new UserProfileImageGcsStorage(
            sp.GetRequiredService<IFirebaseGcsStorage>(),
            sp.GetRequiredService<IOptions<FirebaseStorageOptions>>());
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    return new UserProfileImageDiskStorage(root);
});

// SignalR
builder.Services.AddSignalR();

// Register SignalR-based notification service (after SignalR is configured)
builder.Services.AddScoped<SenorArroz.Application.Common.Interfaces.IOrderNotificationService, SenorArroz.API.Services.OrderNotificationService>();
builder.Services.AddScoped<SenorArroz.Application.Common.Interfaces.IWhatsAppNotificationService, SenorArroz.API.Services.WhatsAppNotificationService>();
builder.Services.AddSingleton<SenorArroz.API.Services.WhatsAppAiWorkQueue>();
builder.Services.AddSingleton<IWhatsAppAiWorkQueue>(sp => sp.GetRequiredService<SenorArroz.API.Services.WhatsAppAiWorkQueue>());
builder.Services.AddSingleton<SenorArroz.API.Services.WhatsAppAiTelemetryQueue>();
builder.Services.Configure<SenorArroz.API.Services.WhatsAppAiTelemetryWorkerOptions>(builder.Configuration.GetSection(SenorArroz.API.Services.WhatsAppAiTelemetryWorkerOptions.SectionName));
builder.Services.AddSingleton<IWhatsAppAiTelemetryQueue>(sp => sp.GetRequiredService<SenorArroz.API.Services.WhatsAppAiTelemetryQueue>());
builder.Services.AddHostedService<SenorArroz.API.Services.WhatsAppAiTelemetryWorker>();
builder.Services.AddHostedService<SenorArroz.API.Services.WhatsAppAiBackgroundService>();
builder.Services.AddHostedService<SenorArroz.API.Services.WhatsAppAiRecoveryService>();
builder.Services.AddScoped<IWhatsAppAutomaticMessageSender, SenorArroz.API.Services.WhatsAppAutomaticMessageSender>();
builder.Services.AddScoped<SenorArroz.API.Services.IPrintAgentNotificationService, SenorArroz.API.Services.PrintAgentNotificationService>();
builder.Services.AddScoped<SenorArroz.Application.Common.Interfaces.IPrintAgentNotifier>(
    sp => sp.GetRequiredService<SenorArroz.API.Services.IPrintAgentNotificationService>());

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
const string sessionReplacedItemKey = "exclusive-delivery-session-replaced";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Set to true in production
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero // Remove delay of token when expire
    };

    // Add custom events for better error handling
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            var role = principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (!string.Equals(role, "Deliveryman", StringComparison.OrdinalIgnoreCase))
                return;

            var userIdValue = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdValue, out var userId))
            {
                context.Fail("Usuario de la sesión inválido.");
                return;
            }

            var sessionValue = principal?.FindFirst("session_id")?.Value;
            Guid? sessionId = Guid.TryParse(sessionValue, out var parsedSessionId)
                ? parsedSessionId
                : null;
            var authRepository = context.HttpContext.RequestServices
                .GetRequiredService<IAuthRepository>();
            if (!await authRepository.IsSessionCurrentAsync(
                    userId,
                    sessionId,
                    context.HttpContext.RequestAborted))
            {
                context.HttpContext.Items[sessionReplacedItemKey] = true;
                context.Fail("SESSION_REPLACED");
            }
        },
        OnAuthenticationFailed = context =>
        {
            if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
            {
                context.Response.Headers.Append("Token-Expired", "true");

            }
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            var sessionWasReplaced =
                context.HttpContext.Items.ContainsKey(sessionReplacedItemKey);
            if (sessionWasReplaced)
                context.Response.Headers.Append("X-Session-Replaced", "true");
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = "Unauthorized",
                code = sessionWasReplaced ? "SESSION_REPLACED" : "UNAUTHORIZED",
                message = sessionWasReplaced
                    ? "Tu sesión fue iniciada en otro dispositivo. Inicia sesión nuevamente."
                    : "Token inválido o expirado"
            });
            return context.Response.WriteAsync(result);
        },
        OnForbidden = context =>
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = "Forbidden",
                message = "No tienes permisos para acceder a este recurso"
            });
            return context.Response.WriteAsync(result);
        }
    };
});

builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .WithOrigins(
                "https://senorarroz.com",
                "https://www.senorarroz.com",
                "http://localhost:5173", 
                "http://localhost:5174", 
                "http://localhost:3000",
                "https://senorarroz.up.railway.app",
                "https://senorarrozapi.up.railway.app",
                "https://api.senorarroz.com"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var globalPermit = Math.Max(1, builder.Configuration.GetValue("RateLimiting:Global:PermitLimit", 200));
var globalWindowSec = Math.Max(1, builder.Configuration.GetValue("RateLimiting:Global:WindowSeconds", 60));
var authPermit = Math.Max(1, builder.Configuration.GetValue("RateLimiting:Auth:PermitLimit", 10));
var authWindowSec = Math.Max(1, builder.Configuration.GetValue("RateLimiting:Auth:WindowSeconds", 60));
var rappiWebhookPermit = Math.Max(1, builder.Configuration.GetValue("RateLimiting:RappiWebhook:PermitLimit", 600));
var rappiWebhookWindowSec = Math.Max(1, builder.Configuration.GetValue("RateLimiting:RappiWebhook:WindowSeconds", 60));
var globalWindow = TimeSpan.FromSeconds(globalWindowSec);
var authWindow = TimeSpan.FromSeconds(authWindowSec);
var rappiWebhookWindow = TimeSpan.FromSeconds(rappiWebhookWindowSec);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, cancellationToken) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var sec = (int)Math.Ceiling(retryAfter.TotalSeconds);
            if (sec > 0)
                ctx.HttpContext.Response.Headers.RetryAfter = sec.ToString(NumberFormatInfo.InvariantInfo);
        }

        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "TooManyRequests",
            message = "Demasiadas solicitudes. Intente más tarde."
        }, cancellationToken);
    };

    options.AddPolicy("auth-sensitive", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = authPermit,
                Window = authWindow,
                QueueLimit = 0
            });
    });

    options.AddPolicy("rappi-webhook", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"rappi:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rappiWebhookPermit,
                Window = rappiWebhookWindow,
                QueueLimit = 0
            });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var path = httpContext.Request.Path;
        var pv = path.Value ?? string.Empty;
        if (path.StartsWithSegments("/swagger")
            || path.StartsWithSegments("/swagger-ui")
            || path.StartsWithSegments("/hubs")
            || path.StartsWithSegments("/api/integrations/rappi/webhooks")
            || string.Equals(pv, "/", StringComparison.Ordinal)
            || string.Equals(pv, "/index.html", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter<string>("exempt");
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = globalPermit,
                Window = globalWindow,
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
// Swagger habilitado en Development y Production para facilitar testing
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SenorArroz API v1");
    c.RoutePrefix = string.Empty; // Serve Swagger UI at the app's root
});

// Global exception handling middleware
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseCors("AllowAll");

// Authentication & Authorization
app.UseAuthentication();
app.UseMiddleware<DeliveryAppVersionMiddleware>();
app.UseAuthorization();

app.UseRateLimiter();

// Custom JWT middleware for additional user context
app.UseMiddleware<JwtMiddleware>();

app.MapControllers();

// Map SignalR Hub
app.MapHub<SenorArroz.API.Hubs.OrderHub>("/hubs/orders");
app.MapHub<SenorArroz.API.Hubs.WhatsAppHub>("/hubs/whatsapp");
app.MapHub<SenorArroz.API.Hubs.PrintAgentHub>("/hubs/print-agent");

app.Run();
