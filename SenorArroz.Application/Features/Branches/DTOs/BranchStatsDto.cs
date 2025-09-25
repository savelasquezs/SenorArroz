namespace SenorArroz.Application.Features.Branches.DTOs;

public class BranchStatsDto
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;

    // User Statistics
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int AdminUsers { get; set; }
    public int CashierUsers { get; set; }
    public int KitchenUsers { get; set; }
    public int DeliveryUsers { get; set; }

    // Customer Statistics
    public int TotalCustomers { get; set; }
    public int ActiveCustomers { get; set; }
    public int CustomersThisMonth { get; set; }

    // Operational Statistics
    public int TotalNeighborhoods { get; set; }
    public int TotalOrders { get; set; }
    public int OrdersThisMonth { get; set; }

    // Financial Statistics (if needed)
    public decimal AverageDeliveryFee { get; set; }
    public int MinDeliveryFee { get; set; }
    public int MaxDeliveryFee { get; set; }
}