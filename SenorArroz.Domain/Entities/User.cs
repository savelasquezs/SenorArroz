using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class User : BaseEntity
{
    public int BranchId { get; set; }
    public UserRole? Role { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool Active { get; set; } = true;

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<Order> TakenOrders { get; set; } = new List<Order>();
    public virtual ICollection<Order> DeliveryOrders { get; set; } = new List<Order>();
}