using System.Text.Json;
using SenorArroz.Application.Common.Printing;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models.Printing;

namespace SenorArroz.Tests;

public class PrintTicketPayloadBuilderTests
{
    [Fact]
    public void BuildOrder_maps_order_notes_to_OrderNotes()
    {
        var order = new Order
        {
            Id = 1,
            BranchId = 1,
            TakenById = 1,
            Status = OrderStatus.InPreparation,
            Type = OrderType.Onsite,
            Notes = "  Sin cebolla en el arroz  ",
            Subtotal = 20000,
            Total = 20000,
            CreatedAt = DateTime.UtcNow,
            Branch = new Branch { Id = 1, Name = "Prueba" },
            OrderDetails = new List<OrderDetail>(),
            BankPayments = new List<BankPayment>(),
            AppPayments = new List<AppPayment>(),
        };

        var payload = PrintTicketPayloadBuilder.BuildOrder(
            order,
            PrintJobKind.Kitchen,
            DateTime.UtcNow,
            null,
            "Marca",
            "Pie",
            loyalty: null);

        Assert.Equal("Sin cebolla en el arroz", payload.OrderNotes);
    }

    [Fact]
    public void BuildOrder_OrderNotes_is_null_when_notes_blank()
    {
        var order = new Order
        {
            Id = 2,
            BranchId = 1,
            TakenById = 1,
            Status = OrderStatus.Taken,
            Type = OrderType.Delivery,
            Notes = "   ",
            Subtotal = 0,
            Total = 0,
            CreatedAt = DateTime.UtcNow,
            Branch = new Branch { Id = 1, Name = "P" },
            OrderDetails = new List<OrderDetail>(),
            BankPayments = new List<BankPayment>(),
            AppPayments = new List<AppPayment>(),
        };

        var payload = PrintTicketPayloadBuilder.BuildOrder(
            order,
            PrintJobKind.Kitchen,
            DateTime.UtcNow,
            null,
            "M",
            "F",
            null);

        Assert.Null(payload.OrderNotes);
    }

    [Fact]
    public void Print_payload_JSON_includes_orderNotes_and_roundtrips_like_print_agent()
    {
        var order = new Order
        {
            Id = 99,
            BranchId = 1,
            TakenById = 1,
            Status = OrderStatus.InPreparation,
            Type = OrderType.Onsite,
            Notes = "Entregar con cubiertos",
            Subtotal = 1000,
            Total = 1000,
            CreatedAt = DateTime.UtcNow,
            Branch = new Branch { Id = 1, Name = "B" },
            OrderDetails = new List<OrderDetail>(),
            BankPayments = new List<BankPayment>(),
            AppPayments = new List<AppPayment>(),
        };
        var one = PrintTicketPayloadBuilder.BuildOrder(
            order,
            PrintJobKind.Kitchen,
            DateTime.UtcNow,
            null,
            "Marca",
            "Pie",
            null);
        var batch = new PrintTicketPayloadBatchV1 { Version = 1, Orders = [one] };
        var json = PrintTicketPayloadJson.SerializeBatch(batch);
        Assert.Contains("orderNotes", json, StringComparison.Ordinal);
        Assert.Contains("cubiertos", json, StringComparison.Ordinal);
        var agentLikeOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var back = JsonSerializer.Deserialize<PrintTicketPayloadBatchV1>(json, agentLikeOpts);
        Assert.Equal("Entregar con cubiertos", back?.Orders[0].OrderNotes);
    }
}
