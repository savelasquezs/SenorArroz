using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.API.Hosting;

public static class WhatsAppProcessingRegistration
{
    public static IServiceCollection ConfigureWhatsAppProcessing(this IServiceCollection services,
        IConfiguration configuration, bool worker)
    {
        services.AddOptions<WhatsAppProcessingOptions>().Bind(configuration.GetSection(WhatsAppProcessingOptions.SectionName))
            .Validate(x => x.IsValid(), "Invalid WhatsAppProcessing configuration.").ValidateOnStart();
        var options = configuration.GetSection(WhatsAppProcessingOptions.SectionName).Get<WhatsAppProcessingOptions>() ?? new();
        if (worker && !options.External)
            throw new InvalidOperationException("The WhatsApp worker requires WhatsAppProcessing__Mode=External.");
        if (options.External && options.WorkerKey.Length is < 32 or > 256)
            throw new InvalidOperationException("External processing requires a worker key of at least 32 characters.");
        if (worker && (!Uri.TryCreate(options.BusinessApiUrl, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.Scheme != "https" && !(uri.Scheme == "http" && (uri.IsLoopback || uri.Host.EndsWith(".railway.internal", StringComparison.Ordinal)))))
            throw new InvalidOperationException("Configure the business API using HTTPS, loopback or the private Railway network.");
        services.AddScoped<WhatsAppWebhookProcessor>();
        services.AddScoped<WhatsAppManualOperations>();
        services.AddScoped(sp => new Lazy<IAgentToolExecutor>(() => sp.GetRequiredService<IAgentToolExecutor>()));
        services.AddScoped<WhatsAppProcessingStore>();
        if (options.External)
        {
            services.AddSingleton<WhatsAppDatabaseSignal>();
            services.AddScoped<WhatsAppManualWorkQueue>();
        }
        var consumers = new HashSet<Type>
        {
            typeof(WhatsAppAiBackgroundService), typeof(WhatsAppAiRecoveryService),
            typeof(WhatsAppAiTelemetryWorker), typeof(WhatsAppCommerceOutboxWorker)
        };
        if (worker || options.External)
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService)
                && (worker || x.ImplementationType is not null && consumers.Contains(x.ImplementationType))).ToList())
                services.Remove(descriptor);
        if (options.External)
            services.AddHostedService(sp => sp.GetRequiredService<WhatsAppDatabaseSignal>());
        if (worker)
        {
            services.AddHostedService<WhatsAppWebhookWorker>();
            services.AddHostedService<WhatsAppProcessingMaintenance>();
            services.AddHostedService<WhatsAppAiBackgroundService>();
            services.AddHostedService<WhatsAppAiRecoveryService>();
            services.AddHostedService<WhatsAppAiTelemetryWorker>();
            services.AddHostedService<WhatsAppCommerceOutboxWorker>();
            services.AddHttpClient<IAgentToolExecutor, WhatsAppBusinessGateway>(client => client.Timeout = TimeSpan.FromSeconds(25))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        }
        else if (options.External)
            services.AddHostedService<WhatsAppRealtimeRelay>();
        return services;
    }
}
