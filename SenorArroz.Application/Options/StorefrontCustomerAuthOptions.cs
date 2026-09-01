namespace SenorArroz.Application.Options;

public sealed class StorefrontCustomerAuthOptions
{
    public int TenantId { get; set; } = 1;
    public int AuthenticationBranchId { get; set; }
    public string TemplateName { get; set; } = "customers_web_authentication";
    public string TemplateLanguage { get; set; } = "es";
    public string HmacSecret { get; set; } = string.Empty;
    public int CodeLifetimeMinutes { get; set; } = 10;
    public int ResendSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 5;
    public int MaxSendsPerPhonePerHour { get; set; } = 5;
    public int MaxSendsPerIpPerHour { get; set; } = 20;
    public int SessionLifetimeDays { get; set; } = 30;
}
