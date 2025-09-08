using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Infrastructure.Services;

public class TokenCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TokenCleanupService> _logger;
    private readonly TimeSpan _period = TimeSpan.FromHours(1); // Ejecutar cada hora

    public TokenCleanupService(IServiceProvider serviceProvider, ILogger<TokenCleanupService> logger)
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
            var refreshTokenRepository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

            await refreshTokenRepository.DeleteExpiredTokensAsync();

            _logger.LogInformation("Token cleanup completed at {Time}", DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during token cleanup at {Time}", DateTimeOffset.Now);
        }
    }
}