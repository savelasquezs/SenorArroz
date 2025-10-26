using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AutoMapper;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Enums;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public class ReservationNotificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReservationNotificationService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    public ReservationNotificationService(
        IServiceProvider serviceProvider,
        ILogger<ReservationNotificationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reservation Notification Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndNotifyReservations();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking reservations");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Reservation Notification Service stopped");
    }

    private async Task CheckAndNotifyReservations()
    {
        using var scope = _serviceProvider.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var notificationService = scope.ServiceProvider.GetRequiredService<IOrderNotificationService>();
        var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();

        var now = DateTime.UtcNow;
        var twoHoursFromNow = now.AddHours(2);

        // Buscar reservas que estén a 2 horas o menos y aún no estén en preparación
        var reservations = await orderRepository.GetReservationsDueForPreparation(
            now, 
            twoHoursFromNow, 
            OrderStatus.Taken);

        foreach (var reservation in reservations)
        {
            var orderDto = mapper.Map<OrderDto>(reservation);
            await notificationService.NotifyReservationToKitchen(orderDto);
            
            _logger.LogInformation(
                "Notified kitchen about reservation {OrderId} for branch {BranchId}", 
                reservation.Id, 
                reservation.BranchId);
        }
    }
}

