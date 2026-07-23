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
        Assert.Equal(payload.OrderNotes, payload.OrderLevelNotes);
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
        Assert.Contains("\"notes\":", json, StringComparison.Ordinal);
        Assert.Contains("cubiertos", json, StringComparison.Ordinal);
        var agentLikeOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var back = JsonSerializer.Deserialize<PrintTicketPayloadBatchV1>(json, agentLikeOpts);
        Assert.Equal("Entregar con cubiertos", back?.Orders[0].OrderNotes);
        Assert.Equal("Entregar con cubiertos", back?.Orders[0].OrderLevelNotes);
    }

    [Fact]
    public void Delivery_payload_preserves_operational_fields_and_cash_to_collect()
    {
        var reservedFor = new DateTime(2026, 7, 24, 18, 0, 0, DateTimeKind.Utc);
        var snapshot = new DeliveryPrintSnapshot
        {
            Id = 123,
            BranchId = 1,
            Status = OrderStatus.OnTheWay,
            Type = OrderType.Reservation,
            DeliveryManId = 7,
            CustomerName = "Cliente",
            CustomerPhone1 = "3000000000",
            AddressDescription = "Calle 1 # 2-3",
            AddressAdditionalInfo = "Casa azul",
            NeighborhoodName = "Centro",
            Subtotal = 40000,
            Total = 45000,
            DeliveryFee = 5000,
            Notes = "Nota general",
            ReservedFor = reservedFor,
            CreatedAt = reservedFor.AddHours(-2),
            Lines =
            [
                new DeliveryPrintLineSnapshot
                {
                    Id = 1,
                    ProductName = "Arroz",
                    Quantity = 2,
                    UnitPrice = 20000,
                    Subtotal = 40000,
                    Notes = "Sin cebolla",
                },
            ],
            BankPayments =
            [
                new DeliveryPrintBankPaymentSnapshot
                {
                    BankName = "Banco",
                    Amount = 10000,
                    IsVerified = true,
                },
            ],
            AppPayments =
            [
                new DeliveryPrintAppPaymentSnapshot
                {
                    AppName = "App",
                    Amount = 5000,
                },
            ],
        };

        var order = DeliveryPrintPayloadBuilder
            .BuildBatch([snapshot], DateTime.UtcNow)
            .Orders
            .Single();

        Assert.Equal("Calle 1 # 2-3", order.Customer?.AddressDescription);
        Assert.Equal("Casa azul", order.Customer?.AddressAdditionalInfo);
        Assert.Equal("Centro", order.Customer?.NeighborhoodName);
        Assert.Equal("Sin cebolla", order.Lines.Single().Notes);
        Assert.Equal("Nota general", order.OrderNotes);
        Assert.Equal(reservedFor, order.ReservedFor);
        Assert.Equal(30000, order.Totals.CashToCollect);
    }

    [Fact]
    public void Delivery_payload_omits_loyalty_logo_and_commercial_fields()
    {
        var order = DeliveryPrintPayloadBuilder.BuildBatch(
            [
                new DeliveryPrintSnapshot
                {
                    Id = 1,
                    BranchId = 1,
                    Status = OrderStatus.OnTheWay,
                    Type = OrderType.Delivery,
                    Total = 1000,
                    CreatedAt = DateTime.UtcNow,
                },
            ],
            DateTime.UtcNow).Orders.Single();

        Assert.Null(order.LoyaltyDeliveredCount);
        Assert.Null(order.LoyaltyOrdersUntilCycleEnd);
        Assert.Null(order.LoyaltyNextRewardLabel);
        Assert.Null(order.LoyaltyThisOrderGiftLabel);
        Assert.Null(order.LoyaltyRuleName);
        Assert.Null(order.ReceiptLogoPath);
        Assert.Null(order.ReceiptLogoUrl);
        Assert.Null(order.BusinessName);
        Assert.Null(order.BranchNit);
        Assert.Null(order.BranchPhone);
        Assert.Null(order.BranchAddress);
        Assert.Null(order.KitchenFooterMessage);
    }
}
