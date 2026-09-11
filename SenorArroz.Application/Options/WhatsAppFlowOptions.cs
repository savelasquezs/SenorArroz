namespace SenorArroz.Application.Options;

public sealed class WhatsAppFlowOptions
{
    public const string SectionName = "WhatsAppFlow";
    public int TenantId { get; set; } = 1;
    public bool Enabled { get; set; }
    public bool RestrictToAllowlist { get; set; } = true;
    public string[] AllowedPhoneHashes { get; set; } = [];
    public int SessionLifetimeMinutes { get; set; } = 120;
    public string PrivateKey { get; set; } = string.Empty;
    public string PrivateKeyPassphrase { get; set; } = string.Empty;
    public string PaymentReturnUrl { get; set; } = "https://senorarroz.com/pago/whatsapp";
}
