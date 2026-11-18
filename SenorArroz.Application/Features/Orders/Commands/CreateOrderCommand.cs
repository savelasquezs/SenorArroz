using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class CreateOrderCommand : IRequest<OrderDto>
{
    public CreateOrderDto Order { get; set; } = null!;
    public int? WhatsAppCommerceSessionId { get; set; }
    public int? ExpectedWhatsAppDraftVersion { get; set; }
    public long? ExpectedWhatsAppAttentionRevision { get; set; }
    public bool AllowAssignedWhatsAppOperationalBranch { get; set; }
}
