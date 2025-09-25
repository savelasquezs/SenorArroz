namespace SenorArroz.Application.Features.Branches.DTOs;

public class BranchNeighborhoodDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeliveryFee { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Statistics
    public int TotalCustomers { get; set; }
    public int TotalAddresses { get; set; }
}