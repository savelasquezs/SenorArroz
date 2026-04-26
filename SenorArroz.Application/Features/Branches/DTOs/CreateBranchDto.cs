using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Branches.DTOs;

public class CreateBranchDto
{
    [Required(ErrorMessage = "El nombre de la sucursal es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    public string Name { get; set; } = string.Empty;

    [StringLength(150, ErrorMessage = "El nombre comercial no puede exceder 150 caracteres")]
    public string? BusinessName { get; set; }

    [StringLength(32, ErrorMessage = "El NIT no puede exceder 32 caracteres")]
    [RegularExpression(@"^[\d.\-]*$", ErrorMessage = "El NIT solo puede contener dígitos, puntos y guiones")]
    public string? Nit { get; set; }

    [Required(ErrorMessage = "La dirección es requerida")]
    [StringLength(200, ErrorMessage = "La dirección no puede exceder 200 caracteres")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "El teléfono principal es requerido")]
    [StringLength(10, MinimumLength = 10, ErrorMessage = "El teléfono debe tener exactamente 10 dígitos")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "El teléfono debe contener solo números")]
    public string Phone1 { get; set; } = string.Empty;

    [StringLength(10, MinimumLength = 10, ErrorMessage = "El teléfono secundario debe tener exactamente 10 dígitos")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "El teléfono secundario debe contener solo números")]
    public string? Phone2 { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    [Range(0, 999_999_999, ErrorMessage = "El tope de domicilio gratis debe ser un valor no negativo")]
    public int MaxFreeDeliveryDiscount { get; set; } = 3000;

    [Range(0, 10_080, ErrorMessage = "Los minutos deben estar entre 0 y 10080 (7 días)")]
    public int PosCopyEtaMinMinutes { get; set; } = 30;

    [Range(0, 10_080, ErrorMessage = "El rango de minutos debe estar entre 0 y 10080")]
    public int PosCopyEtaRangeMinutes { get; set; } = 15;
}