using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces;

public sealed record BillingRecipientDto
{
    public int? Id { get; init; }
    [Range(1, 99)] public int DocumentType { get; init; } = 3;
    [Required, MaxLength(32), RegularExpression(@"^[A-Za-z0-9-]+$")] public string Identification { get; init; } = "";
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    [EmailAddress, MaxLength(254)] public string? Email { get; init; }
    public bool IsDefault { get; init; }
}

public interface IElectronicInvoiceService
{
    Task<ElectronicInvoiceDocument> RequestAsync(Order order, BillingRecipientDto recipient, int userId, CancellationToken ct);
    Task ProcessAsync(int documentId, CancellationToken ct);
    Task QueueEmailAsync(int documentId, CancellationToken ct);
    Task PreparePdfAsync(int documentId, CancellationToken ct);
}

public interface IElectronicInvoiceProvider
{
    string Code { get; }
    Task<JsonElement> CompanyAsync(ElectronicInvoiceIntegration integration, CancellationToken ct);
    Task<JsonElement> ResolutionsAsync(ElectronicInvoiceIntegration integration, CancellationToken ct);
    Task<JsonElement> RemainingAsync(ElectronicInvoiceIntegration integration, string resolution, CancellationToken ct);
    Task<JsonElement> LookupRecipientAsync(ElectronicInvoiceIntegration integration, int type, string identification, CancellationToken ct);
    Task SyncEmailAsync(ElectronicInvoiceIntegration integration, CancellationToken ct);
    Task<JsonElement> SubmitAsync(ElectronicInvoiceIntegration integration, string kind, string payload, CancellationToken ct);
    Task<JsonElement?> FindAsync(ElectronicInvoiceIntegration integration, string kind, string number, CancellationToken ct);
    Task<byte[]> DownloadAsync(ElectronicInvoiceIntegration integration, string kind, string reference, string format, CancellationToken ct);
}
