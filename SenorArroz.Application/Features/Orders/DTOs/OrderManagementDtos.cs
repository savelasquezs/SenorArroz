using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.DTOs;

public sealed record RemoteOrderBranchDto(int Id, string Name, int SampleOrderId);
public sealed record OrderBankOptionDto(int Id, string Name, int BranchId, bool Active, string? ImageUrl, BankType Type);
public sealed record OrderAppOptionDto(int Id, string Name, int BankId, string BankName, int BranchId, bool Active, string? ImageUrl);
public sealed record OrderDeliverymanOptionDto(int Id, string Name);
public sealed record OrderOperationOptionsDto(int BranchId, string BranchName,
    IReadOnlyList<OrderBankOptionDto> Banks,
    IReadOnlyList<OrderAppOptionDto> Apps,
    IReadOnlyList<OrderDeliverymanOptionDto> Deliverymen, decimal MaxFreeDeliveryDiscount);
public sealed record OrderDriverLocationDto(decimal Latitude, decimal Longitude, DateTime RecordedAt, bool IsFresh);
public sealed record OrderTrackingDto(int OrderId, OrderStatus Status, int? DeliveryManId,
    int? DeliveryRouteId, string? DeliveryManName, long ManagementVersion, OrderDriverLocationDto? Location);
