using System.ComponentModel.DataAnnotations;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.BranchPrintSettings.DTOs;

public class UpdateBranchPrintSettingsDto
{
    [StringLength(80)]
    public string? KitchenHeaderLine1 { get; set; }
    [StringLength(80)]
    public string? KitchenHeaderLine2 { get; set; }
    public bool ShowKitchenOrderNumber { get; set; } = true;
    public bool ShowKitchenTime { get; set; } = true;
    public bool ShowKitchenNotes { get; set; } = true;

    public bool DeliveryShowLineSubtotals { get; set; } = true;
    public bool DeliveryShowPayments { get; set; } = true;
    public bool DeliveryShowLoyaltyFooter { get; set; } = true;
    public bool CashierMirrorDeliveryLayout { get; set; }

    [StringLength(200)]
    public string? FooterMessageKitchen { get; set; }
    [StringLength(200)]
    public string? FooterMessageDelivery { get; set; }
    [StringLength(200)]
    public string? FooterMessageCashier { get; set; }

    [Range(40, 120)]
    public short PaperWidthMmKitchen { get; set; } = 58;

    [Range(40, 120)]
    public short PaperWidthMmDelivery { get; set; } = 58;

    [Range(40, 120)]
    public short PaperWidthMmCashier { get; set; } = 58;

    public bool EnableKitchenJobs { get; set; } = true;
    public bool EnableDeliveryJobs { get; set; } = true;
    public bool EnableCashierJobs { get; set; }
    public KitchenAutoPrintTrigger? KitchenAutoPrintTrigger { get; set; }

    [StringLength(128)]
    public string? PrinterQueueKitchen { get; set; }
    [StringLength(128)]
    public string? PrinterQueueDelivery { get; set; }
    [StringLength(128)]
    public string? PrinterQueueCashier { get; set; }
}
