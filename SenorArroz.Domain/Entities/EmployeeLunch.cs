using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class EmployeeLunch : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public int UserId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int StartedByUserId { get; set; }
    public int? EndedByUserId { get; set; }
    public User User { get; set; } = null!;
}
