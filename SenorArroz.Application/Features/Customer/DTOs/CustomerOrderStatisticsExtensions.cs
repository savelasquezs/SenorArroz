using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.DTOs;

internal static class CustomerOrderStatisticsExtensions
{
    public static void ApplyOrderStatistics(this CustomerDto dto, CustomerOrderStatistics? stats)
    {
        dto.TotalOrders = stats?.TotalOrders ?? 0;
        dto.FirstOrderDate = stats?.FirstOrderDate;
        dto.LastOrderDate = stats?.LastOrderDate;
        dto.TotalAccumulated = stats?.TotalAccumulated ?? 0;
        dto.LoyaltyDeliveredCount = stats?.DeliveredOrders ?? 0;
    }
}
