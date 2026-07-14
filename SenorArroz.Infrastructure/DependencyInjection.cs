using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Infrastructure.Services;
using SenorArroz.Infrastructure.Storage;
using SenorArroz.Infrastructure.WhatsApp;
using Microsoft.Extensions.Hosting;



namespace SenorArroz.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DeliveryRouteOptions>(configuration.GetSection(DeliveryRouteOptions.SectionName));
        services.Configure<GoogleMapsRouteOptions>(configuration.GetSection(GoogleMapsRouteOptions.SectionName));
        services.Configure<ApiPublicOptions>(configuration.GetSection(ApiPublicOptions.SectionName));
        services.Configure<BrandingOptions>(configuration.GetSection(BrandingOptions.SectionName));
        services.Configure<DeliveryPayrollOptions>(configuration.GetSection(DeliveryPayrollOptions.SectionName));
        services.Configure<FirebaseStorageOptions>(configuration.GetSection(FirebaseStorageOptions.SectionName));
        services.Configure<WhatsAppCloudOptions>(configuration.GetSection(WhatsAppCloudOptions.SectionName));
        services.Configure<WhatsAppAiOrchestratorOptions>(configuration.GetSection(WhatsAppAiOrchestratorOptions.SectionName));
        services.Configure<WhatsAppAiPricingOptions>(configuration.GetSection(WhatsAppAiPricingOptions.SectionName));
        services.PostConfigure<WhatsAppCloudOptions>(options =>
        {
            options.AccessToken = FirstNonEmpty(configuration["WHATSAPP_TOKEN"], options.AccessToken);
            options.BusinessAccountId = FirstNonEmpty(configuration["WHATSAPP_BUSINESS_ACCOUNT_ID"], options.BusinessAccountId);
            options.PhoneNumberId = FirstNonEmpty(configuration["WHATSAPP_PHONE_NUMBER_ID"], options.PhoneNumberId);
            options.GraphApiVersion = FirstNonEmpty(configuration["GRAPH_API_VERSION"], options.GraphApiVersion) ?? options.GraphApiVersion;
        });
        services.AddSingleton<IFirebaseGcsStorage, FirebaseGcsStorageService>();

        // FCM Push Notifications
        services.AddHttpClient<FcmPushService>();
        services.AddScoped<IFcmPushService, FcmPushService>();

        // Database
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<IPrintQueueService, PrintQueueService>();

        services.AddHttpClient<GoogleRoutesDrivingMetricsService>();
        services.AddScoped<IGoogleRoutesDrivingMetricsService>(sp => sp.GetRequiredService<GoogleRoutesDrivingMetricsService>());
        services.AddScoped<IDeliveryRouteWorkflowService, DeliveryRouteWorkflowService>();
        services.AddHttpClient<IWhatsAppCloudClient, WhatsAppCloudClient>();
        services.AddHttpClient<OpenAiProvider>();
        services.AddHttpClient<GeminiProvider>();
        services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<OpenAiProvider>());
        services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<GeminiProvider>());
        services.AddScoped<IAiProviderResolver, AiProviderResolver>();
        services.AddSingleton<IAiApiKeyProvider, EnvironmentAiApiKeyProvider>();
        services.AddScoped<IAiChatProvider>(sp => sp.GetRequiredService<OpenAiProvider>());
        services.AddScoped<IAiChatProvider>(sp => sp.GetRequiredService<GeminiProvider>());
        services.AddScoped<IAiChatProviderResolver, AiChatProviderResolver>();
        services.AddScoped<RegisteredNeighborhoodResolver>();
        services.AddHttpClient<GoogleAddressGeocoder>();
        services.AddScoped<CustomerAddressResolutionService>();
        services.AddScoped<IWhatsAppSimpleOrderStateService, WhatsAppSimpleOrderStateService>();
        services.AddScoped<IAgentTool, ApplyOrderActionAgentTool>();
        services.AddScoped<IAgentTool, SendProductDetailsAgentTool>();
        services.AddScoped<IAgentTool, SendMenuAgentTool>();
        services.AddScoped<RequestHumanAssistanceAgentTool>();
        services.AddScoped<IAgentTool>(sp => sp.GetRequiredService<RequestHumanAssistanceAgentTool>());
        services.AddScoped<IAgentTool, ResolveAndCreateCustomerAddressAgentTool>();
        services.AddScoped<IAgentTool, CreateCustomerAgentTool>();
        services.AddScoped<IWhatsAppAiMessageClaimer, WhatsAppAiMessageClaimer>();

        // Repositories
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
        // Customer Repositories
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IAddressRepository, AddressRepository>();
        services.AddScoped<INeighborhoodRepository, NeighborhoodRepository>();

        // Deliveryman Repositories
        services.AddScoped<IDeliverymanAdvanceRepository, DeliverymanAdvanceRepository>();

        //product Repositories
        services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();

        // Expense Repositories
        services.AddScoped<IExpenseCategoryRepository, ExpenseCategoryRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IExpenseHeaderRepository, ExpenseHeaderRepository>();
        services.AddScoped<IExpenseDashboardRepository, ExpenseDashboardRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();

        // Bank and Payment Repositories
        services.AddScoped<IBankRepository, BankRepository>();
        services.AddScoped<IBankLedgerService, BankLedgerService>();
        services.AddScoped<IAppRepository, AppRepository>();
        services.AddScoped<IBankPaymentRepository, BankPaymentRepository>();
        services.AddScoped<IAppPaymentRepository, AppPaymentRepository>();
        services.AddScoped<IBankTransferRepository, BankTransferRepository>();

        // Order Repositories
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ILoyaltyCycleStepRepository, LoyaltyCycleStepRepository>();
        services.AddScoped<IReservationDepositRepository, ReservationDepositRepository>();

        // Cash Register Repositories
        services.AddScoped<ICashRegisterClosureRepository, CashRegisterClosureRepository>();

        // Services
        services.AddHttpContextAccessor();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<SmtpEmailDeliveryService>();
        services.AddScoped<IEmailService, EmailService>();
        // OrderNotificationService will be registered in Program.cs after SignalR setup

        // Background Services
        services.AddHostedService<EmailOutboxWorker>();
        services.AddHostedService<TokenCleanupService>();
        services.AddHostedService<PasswordResetCleanupService>();
        services.AddHostedService<ReservationNotificationService>();
        services.AddHostedService<DeliveryRouteConsolidationWorker>();

        return services;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
    }
}
