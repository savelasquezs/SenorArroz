using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;
using System.Text.Json;

namespace SenorArroz.Domain.Entities;

public class Order : BaseEntity
{
    public int BranchId { get; set; }
    public int TakenById { get; set; }
    public int? CustomerId { get; set; }
    public int? AddressId { get; set; }
    public int? LoyaltyRuleId { get; set; }
    public int? DeliveryManId { get; set; }
    public string? GuestName { get; set; }

    public OrderType? Type { get; set; }
    public int? DeliveryFee { get; set; }
    public DateTime? ReservedFor { get; set; }
    public OrderStatus Status { get; set; }

    // JSONB field para timestamps - mapea directamente a "status_times"
    public string StatusTimes { get; set; } = "{}";

    public int Subtotal { get; set; } = 0;
    public int Total { get; set; } = 0;
    public int DiscountTotal { get; set; } = 0;
    public string? Notes { get; set; }
    public string? CancelledReason { get; set; }

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual User TakenBy { get; set; } = null!;
    public virtual Customer? Customer { get; set; }
    public virtual Address? Address { get; set; }
    public virtual LoyaltyRule? LoyaltyRule { get; set; }
    public virtual User? DeliveryMan { get; set; }

    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    public virtual ICollection<BankPayment> BankPayments { get; set; } = new List<BankPayment>();
    public virtual ICollection<AppPayment> AppPayments { get; set; } = new List<AppPayment>();

    // Helper methods para StatusTimes
    public Dictionary<string, DateTime> GetStatusTimes()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(StatusTimes) ?? new Dictionary<string, DateTime>();
        }
        catch
        {
            return new Dictionary<string, DateTime>();
        }
    }

    public void SetStatusTimes(Dictionary<string, DateTime> statusTimes)
    {
        StatusTimes = JsonSerializer.Serialize(statusTimes);
    }

    public void AddStatusTime(OrderStatus status, DateTime timestamp)
    {
        var statusTimes = GetStatusTimes();
        statusTimes[status.ToString().ToLowerInvariant()] = timestamp;
        SetStatusTimes(statusTimes);
    }
}
