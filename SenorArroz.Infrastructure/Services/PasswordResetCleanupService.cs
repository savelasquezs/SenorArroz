using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Infrastructure.Services;

public class PasswordResetCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PasswordResetCleanupService> _logger;
    private readonly TimeSpan _period = TimeSpan.FromHours(6); // Run every 6 hours

    public PasswordResetCleanupService(
        IServiceProvider serviceProvider,
        ILogger<PasswordResetCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupExpiredTokensAsync();
            await Task.Delay(_period, stoppingToken);
        }
    }

    private async Task CleanupExpiredTokensAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var passwordResetRepository = scope.ServiceProvider.GetRequiredService<IPasswordResetRepository>();

            await passwordResetRepository.DeleteExpiredTokensAsync();

            _logger.LogInformation("Password reset token cleanup completed at {Time}", DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during password reset token cleanup at {Time}", DateTimeOffset.Now);
        }
    }
}