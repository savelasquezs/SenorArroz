using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AutoMapper;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public class ReservationNotificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReservationNotificationService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(2);

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
        await TenantWorkerRunner.RunForEachActiveTenantAsync(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            (services, _) => CheckTenantReservationsAsync(services),
            CancellationToken.None);
    }

    private async Task CheckTenantReservationsAsync(IServiceProvider services)
    {
        var orderRepository = services.GetRequiredService<IOrderRepository>();
        var notificationService = services.GetRequiredService<IOrderNotificationService>();
        var mapper = services.GetRequiredService<IMapper>();
        var clock = services.GetRequiredService<IClock>();
        var db = services.GetRequiredService<IApplicationDbContext>();
        var printQueue = services.GetRequiredService<IPrintQueueService>();

        var now = clock.UtcNow;
        var twoHoursFromNow = now.AddHours(2);

        // Buscar pedidos donde prepare_at <= now (o fallback reserved_for-1h) y aún no notificados
        var reservations = await orderRepository.GetReservationsDueForPreparation(
            now,
            twoHoursFromNow,
            OrderStatus.Taken);

        foreach (var reservation in reservations)
        {
            var orderDto = mapper.Map<OrderDto>(reservation);
            await notificationService.NotifyReservationToKitchen(orderDto);

            var shouldPrint = await db.BranchPrintSettings.AsNoTracking().AnyAsync(
                s => s.BranchId == reservation.BranchId
                    && s.EnableKitchenJobs
                    && s.KitchenAutoPrintTrigger == BranchPrintSettings.KitchenAutoPrintWhenOrderCreated);
            if (shouldPrint)
            {
                try
                {
                    await printQueue.EnqueueAsync(
                        reservation.BranchId,
                        PrintJobKind.Kitchen,
                        [reservation.Id]);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "No se encoló comanda de cocina para reserva {OrderId} al llegar su hora de preparación.",
                        reservation.Id);
                }
            }

            // Marcar como notificado para evitar duplicados
            reservation.PreparedNotifiedAt = clock.UtcNow;
            await orderRepository.UpdateAsync(reservation);

            _logger.LogInformation(
                "Notified kitchen about reservation {OrderId} for branch {BranchId}",
                reservation.Id,
                reservation.BranchId);
        }
    }
}

