using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.BranchPrintSettings.Commands;

public class UpdateBranchPrintSettingsHandler : IRequestHandler<UpdateBranchPrintSettingsCommand, BranchPrintSettingsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IMapper _mapper;

    public UpdateBranchPrintSettingsHandler(IApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<BranchPrintSettingsDto> Handle(UpdateBranchPrintSettingsCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.BranchPrintSettings
            .FirstOrDefaultAsync(s => s.BranchId == request.BranchId, cancellationToken);
        if (entity is null)
            throw new NotFoundException($"No hay configuración de impresión para la sucursal {request.BranchId}.");

        var d = request.Dto;
        entity.KitchenHeaderLine1 = d.KitchenHeaderLine1;
        entity.KitchenHeaderLine2 = d.KitchenHeaderLine2;
        entity.ShowKitchenOrderNumber = d.ShowKitchenOrderNumber;
        entity.ShowKitchenTime = d.ShowKitchenTime;
        entity.ShowKitchenNotes = d.ShowKitchenNotes;
        entity.DeliveryShowLineSubtotals = d.DeliveryShowLineSubtotals;
        entity.DeliveryShowPayments = d.DeliveryShowPayments;
        entity.DeliveryShowLoyaltyFooter = d.DeliveryShowLoyaltyFooter;
        entity.CashierMirrorDeliveryLayout = d.CashierMirrorDeliveryLayout;
        entity.FooterMessageKitchen = d.FooterMessageKitchen;
        entity.FooterMessageDelivery = d.FooterMessageDelivery;
        entity.FooterMessageCashier = d.FooterMessageCashier;
        entity.PaperWidthMm = d.PaperWidthMm;
        entity.EnableKitchenJobs = d.EnableKitchenJobs;
        entity.EnableDeliveryJobs = d.EnableDeliveryJobs;
        entity.EnableCashierJobs = d.EnableCashierJobs;
        entity.PrinterQueueKitchen = NullIfWhiteSpace(d.PrinterQueueKitchen);
        entity.PrinterQueueDelivery = NullIfWhiteSpace(d.PrinterQueueDelivery);
        entity.PrinterQueueCashier = NullIfWhiteSpace(d.PrinterQueueCashier);

        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<BranchPrintSettingsDto>(entity);
    }

    private static string? NullIfWhiteSpace(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
