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

    /// <summary>Minutos mínimos en la ventana de «Copiar mensaje» del POS.</summary>
    public int PosCopyEtaMinMinutes { get; set; } = 30;

    /// <summary>Minutos adicionales al mínimo para el tope (p. ej. 30+15 → 30-45 min).</summary>
    public int PosCopyEtaRangeMinutes { get; set; } = 15;

    public TimeOnly DeliveryTrackingAutoCloseTime { get; set; } = new(21, 0);
    public int DeliveryTrackingLightIntervalSeconds { get; set; } = 300;
    public int DeliveryTrackingActiveIntervalSeconds { get; set; } = 30;
    public int DeliveryTrackingStayThresholdMinutes { get; set; } = 10;
    public int DeliveryTrackingStayRadiusMeters { get; set; } = 50;
    public int DeliveryTrackingAllowedDistanceMeters { get; set; } = 50;
    public int DeliveryTrackingLocationRetentionDays { get; set; } = 3;
    public int DeliveryTrackingIncidentRetentionDays { get; set; } = 15;
    public bool DeliveryAutoCompleteEnabled { get; set; } = true;
    public int DeliveryAutoCompleteArrivalRadiusMeters { get; set; } = 50;
    public int DeliveryAutoCompleteDepartureRadiusMeters { get; set; } = 120;
    public int DeliveryAutoCompleteMinPresenceSeconds { get; set; } = 15;

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
