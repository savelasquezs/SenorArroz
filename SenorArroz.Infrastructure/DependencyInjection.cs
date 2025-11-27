using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SenorArroz.Application.Common.Interfaces;
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
        // Database
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

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

        // Bank and Payment Repositories
        services.AddScoped<IBankRepository, BankRepository>();
        services.AddScoped<IAppRepository, AppRepository>();
        services.AddScoped<IBankPaymentRepository, BankPaymentRepository>();
        services.AddScoped<IAppPaymentRepository, AppPaymentRepository>();

        // Order Repositories
        services.AddScoped<IOrderRepository, OrderRepository>();

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

        return services;
    }
}