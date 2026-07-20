using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DeliveryWorkSession : BaseEntity
{
    public int DeliverymanId { get; set; }
    public int BranchId { get; set; }
    public string DeviceInstallationId { get; set; } = string.Empty;
    public string DevicePlatform { get; set; } = string.Empty;
    public string? DeviceDescription { get; set; }
    public string? AppVersion { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime AutoCloseAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DeliveryWorkSessionEndReason? EndReason { get; set; }
    public DeliveryWorkSessionStatus Status { get; set; } = DeliveryWorkSessionStatus.Active;
    public DateTime LastCommunicationAt { get; set; }

    public virtual User Deliveryman { get; set; } = null!;
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<DeliverymanLocation> Locations { get; set; } = new List<DeliverymanLocation>();
}
