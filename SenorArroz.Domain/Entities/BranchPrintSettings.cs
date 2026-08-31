using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>Configuración de impresión de comandas por sucursal (1:1 con Branch).</summary>
public class BranchPrintSettings
{
    public int BranchId { get; set; }

    public string? KitchenHeaderLine1 { get; set; }
    public string? KitchenHeaderLine2 { get; set; }
    public bool ShowKitchenOrderNumber { get; set; } = true;
    public bool ShowKitchenTime { get; set; } = true;
    public bool ShowKitchenNotes { get; set; } = true;

    public bool DeliveryShowLineSubtotals { get; set; } = true;
    public bool DeliveryShowPayments { get; set; } = true;
    public bool DeliveryShowLoyaltyFooter { get; set; } = true;
    public bool CashierMirrorDeliveryLayout { get; set; }

    public string? FooterMessageKitchen { get; set; }
    public string? FooterMessageDelivery { get; set; }
    public string? FooterMessageCashier { get; set; }

    /// <summary>Legado: se mantiene en BD igual a <see cref="PaperWidthMmKitchen"/> al guardar.</summary>
    public short PaperWidthMm { get; set; } = 58;

    public short PaperWidthMmKitchen { get; set; } = 58;
    public short PaperWidthMmDelivery { get; set; } = 58;
    public short PaperWidthMmCashier { get; set; } = 58;

    public bool EnableKitchenJobs { get; set; } = true;
    public bool EnableDeliveryJobs { get; set; } = true;
    public bool EnableCashierJobs { get; set; }
    public KitchenAutoPrintTrigger KitchenAutoPrintTrigger { get; set; } = KitchenAutoPrintTrigger.WhenMarkedReady;

    /// <summary>Nombre exacto de la cola Windows para cocina (desde panel de impresión).</summary>
    public string? PrinterQueueKitchen { get; set; }
    public string? PrinterQueueDelivery { get; set; }
    public string? PrinterQueueCashier { get; set; }

    /// <summary>Ruta relativa servida por estáticos (ej. /uploads/branch-print/1/logo.png).</summary>
    public string? ReceiptLogoPath { get; set; }

    /// <summary>SHA-256 hex en minúsculas de salt+token; vacío si el agente no está configurado.</summary>
    public string AgentTokenHash { get; set; } = string.Empty;
    public string AgentTokenSalt { get; set; } = string.Empty;
    public DateTime? AgentTokenUpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual Branch Branch { get; set; } = null!;
}
