using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class ElectronicInvoiceIntegration : TenantOwnedEntity
{
    public string Name { get; set; } = "Plemsi";
    public string Provider { get; set; } = "plemsi";
    public string Environment { get; set; } = "sandbox";
    public string EncryptedToken { get; set; } = "";
    public bool IsActive { get; set; }
    public string TaxId { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string BillingEmail { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string Prefix { get; set; } = "";
    public long NextNumber { get; set; } = 1;
    public long LastNumber { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string CreditPrefix { get; set; } = "NC";
    public long NextCreditNumber { get; set; } = 1;
    public bool ProductionReady { get; set; }
    public DateTime? TestedAt { get; set; }
    public string? CompanyJson { get; set; }
    public string? ResolutionsJson { get; set; }
}

public sealed class CustomerBillingRecipient : TenantOwnedEntity
{
    public int CustomerId { get; set; }
    public int DocumentType { get; set; } = 3;
    public string Identification { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class ElectronicInvoiceDocument : TenantOwnedEntity
{
    public int OrderId { get; set; }
    public int IntegrationId { get; set; }
    public int? OriginalDocumentId { get; set; }
    public string Kind { get; set; } = "invoice";
    public string Status { get; set; } = "pending";
    public string Environment { get; set; } = "sandbox";
    public string RecipientJson { get; set; } = "{}";
    public string SnapshotJson { get; set; } = "{}";
    public string? PayloadJson { get; set; }
    public string Prefix { get; set; } = "";
    public long? Number { get; set; }
    public string? Cufe { get; set; }
    public string? ResponseJson { get; set; }
    public string? LastError { get; set; }
    public string EmailStatus { get; set; } = "notRequested";
    public bool CreditRequested { get; set; }
    public string? Reason { get; set; }
    public int RequestedById { get; set; }
    public int? AuthorizedById { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public byte[]? Pdf { get; set; }
    public byte[]? Xml { get; set; }
}
