using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class DeliveryWorkPhone : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string DeviceInstallationId { get; set; } = string.Empty;
}
