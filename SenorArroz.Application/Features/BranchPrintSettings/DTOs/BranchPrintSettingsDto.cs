namespace SenorArroz.Application.Features.BranchPrintSettings.DTOs;

public class BranchPrintSettingsDto
{
    public int BranchId { get; set; }

    public string? KitchenHeaderLine1 { get; set; }
    public string? KitchenHeaderLine2 { get; set; }
    public bool ShowKitchenOrderNumber { get; set; }
    public bool ShowKitchenTime { get; set; }
    public bool ShowKitchenNotes { get; set; }

    public bool DeliveryShowLineSubtotals { get; set; }
    public bool DeliveryShowPayments { get; set; }
    public bool DeliveryShowLoyaltyFooter { get; set; }
    public bool CashierMirrorDeliveryLayout { get; set; }

    public string? FooterMessageKitchen { get; set; }
    public string? FooterMessageDelivery { get; set; }
    public string? FooterMessageCashier { get; set; }

    /// <summary>Igual a cocina; conservado por compatibilidad con clientes antiguos.</summary>
    public short PaperWidthMm { get; set; }

    public short PaperWidthMmKitchen { get; set; }
    public short PaperWidthMmDelivery { get; set; }
    public short PaperWidthMmCashier { get; set; }

    public bool EnableKitchenJobs { get; set; }
    public bool EnableDeliveryJobs { get; set; }
    public bool EnableCashierJobs { get; set; }

    public string? PrinterQueueKitchen { get; set; }
    public string? PrinterQueueDelivery { get; set; }
    public string? PrinterQueueCashier { get; set; }

    /// <summary>Ruta relativa del logo en tickets (estáticos), ej. /uploads/branch-print/1/logo.png.</summary>
    public string? ReceiptLogoPath { get; set; }

    /// <summary>True si hay hash persistido (el agente puede autenticarse si el token fue configurado).</summary>
    public bool AgentTokenConfigured { get; set; }
    public DateTime? AgentTokenUpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
