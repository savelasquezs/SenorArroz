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

        // Database
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<IPrintQueueService, PrintQueueService>();

        services.AddHttpClient<GoogleRoutesDrivingMetricsService>();
        services.AddScoped<IGoogleRoutesDrivingMetricsService>(sp => sp.GetRequiredService<GoogleRoutesDrivingMetricsService>());
        services.AddScoped<IDeliveryRouteWorkflowService, DeliveryRouteWorkflowService>();

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
        services.AddScoped<IEmailService, EmailService>();
        // OrderNotificationService will be registered in Program.cs after SignalR setup

        // Background Services
        services.AddHostedService<TokenCleanupService>();
        services.AddHostedService<PasswordResetCleanupService>();
        services.AddHostedService<ReservationNotificationService>();
        services.AddHostedService<DeliveryRouteConsolidationWorker>();

        return services;
    }
}