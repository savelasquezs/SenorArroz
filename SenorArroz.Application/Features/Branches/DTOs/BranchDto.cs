using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.Application.Features.Branches.DTOs;

public class BranchDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string? Nit { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Phone1 { get; set; } = string.Empty;
    public string? Phone2 { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    /// <summary>Tope COP del descuento “domicilio gratis” en borrador POS.</summary>
    public int MaxFreeDeliveryDiscount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Statistics
    public int TotalUsers { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalNeighborhoods { get; set; }
    public int ActiveUsers { get; set; }
    public int ActiveCustomers { get; set; }

    // Related data
    public List<BranchNeighborhoodDto> Neighborhoods { get; set; } = new();
    public List<BranchUserDto> Users { get; set; } = new();

    public BranchPrintSettingsDto? PrintSettings { get; set; }
}