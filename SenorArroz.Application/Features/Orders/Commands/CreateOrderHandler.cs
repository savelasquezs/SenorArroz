using AutoMapper;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using BranchPrintSettingsEntity = SenorArroz.Domain.Entities.BranchPrintSettings;

namespace SenorArroz.Application.Features.Orders.Commands;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IApplicationDbContext _db;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderNotificationService _notificationService;
    private readonly IClock _clock;
    private readonly IPrintQueueService? _printQueue;
    private readonly ILogger<CreateOrderHandler>? _logger;
    private readonly IInventoryService? _inventory;
    private readonly IBranchContext? _branchContext;

    public CreateOrderHandler(
        IOrderRepository orderRepository,
        IApplicationDbContext db,
        IMapper mapper,
        ICurrentUser currentUser,
        IOrderNotificationService notificationService,
        IClock clock,
        IPrintQueueService? printQueue = null,
        ILogger<CreateOrderHandler>? logger = null,
        IInventoryService? inventory = null,
        IBranchContext? branchContext = null)
    {
        _orderRepository = orderRepository;
        _db = db;
        _mapper = mapper;
        _currentUser = currentUser;
        _notificationService = notificationService;
        _clock = clock;
        _printQueue = printQueue;
        _logger = logger;
        _inventory = inventory;
        _branchContext = branchContext;
    }

    public async Task<OrderDto> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        if (!Roles.IsSuperadminOrAdminOrCashier(_currentUser.Role))
        {
            throw new BusinessException("No tienes permisos para crear pedidos");
        }

        var branchId = request.Order.BranchId > 0
            ? request.Order.BranchId
            : Roles.IsSuperadmin(_currentUser.Role)
                ? 0
                : _currentUser.BranchId;
        if (branchId <= 0)
            throw new BusinessException("Debes especificar la sucursal que atenderá el pedido");

        if (request.AllowAssignedWhatsAppOperationalBranch)
        {
            if (!Roles.IsCashier(_currentUser.Role) || !request.Order.WhatsAppConversationId.HasValue)
                throw new BusinessException("La conversación de WhatsApp y su sucursal operativa son obligatorias.");

            var canCreateOperationalOrder = await _db.WhatsAppConversations.AsNoTracking().AnyAsync(x =>
                x.Id == request.Order.WhatsAppConversationId.Value
                && x.ChannelSettingId.HasValue
                && x.OperationalBranchId == branchId
                && x.AssignedUserId == _currentUser.Id,
                cancellationToken);
            if (!canCreateOperationalOrder)
                throw new BusinessException("No tienes permiso para crear este pedido de WhatsApp en otra sucursal.");
        }

        // Validar que el pedido tenga al menos un producto
        if (request.Order.OrderDetails == null || !request.Order.OrderDetails.Any())
            throw new BusinessException("El pedido debe tener al menos un producto");

        if (request.Order.WhatsAppConversationId.HasValue)
        {
            var canLinkWhatsAppConversation = await _db.WhatsAppConversations.AsNoTracking().AnyAsync(x =>
                x.Id == request.Order.WhatsAppConversationId.Value
                && (Roles.IsSuperadmin(_currentUser.Role)
                    || !x.ChannelSettingId.HasValue && x.BranchId == branchId
                    || x.ChannelSettingId.HasValue && x.OperationalBranchId == branchId
                        && (x.OperationalBranchId == _currentUser.BranchId || x.AssignedUserId == _currentUser.Id)),
                cancellationToken);
            if (!canLinkWhatsAppConversation)
                throw new BusinessException("No tienes permiso para vincular esta conversación de WhatsApp al pedido.");
        }

        // Validate order type specific requirements
        if (request.Order.Type == Domain.Enums.OrderType.Delivery)
        {
            if (!request.Order.CustomerId.HasValue)
                throw new BusinessException("Los pedidos de domicilio requieren un cliente");
            
            if (!request.Order.AddressId.HasValue)
                throw new BusinessException("Los pedidos de domicilio requieren una dirección");
            
            if (string.IsNullOrWhiteSpace(request.Order.GuestName))
                throw new BusinessException("Los pedidos de domicilio requieren el nombre del invitado");
        }

        if (request.Order.Type == Domain.Enums.OrderType.Reservation)
        {
            if (!request.Order.ReservedFor.HasValue)
                throw new BusinessException("Los pedidos de reserva requieren fecha y hora de entrega");
            
            if (string.IsNullOrWhiteSpace(request.Order.GuestName))
                throw new BusinessException("Los pedidos de reserva requieren el nombre del invitado");
        }

        // Validar prepare_at <= reserved_for cuando ambos tienen valor
        if (request.Order.PrepareAt.HasValue && request.Order.ReservedFor.HasValue
            && request.Order.PrepareAt.Value > request.Order.ReservedFor.Value)
        {
            throw new BusinessException("La hora de preparación no puede ser posterior a la hora de entrega");
        }

        var destinationBranch = await _db.Branches.AsNoTracking()
            .Where(x => x.Id == branchId)
            .Select(x => new { x.TenantId, x.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (destinationBranch is null)
            throw new BusinessException("La sucursal destino no existe o no pertenece al restaurante actual");
        if (!destinationBranch.IsActive)
            throw new BusinessException("La sucursal destino no está activa");

        // Idempotency is optional for older clients. New POS clients supply the
        // same UUID on every retry of one draft; never trust the tenant or actor
        // supplied by the payload.
        var posCreationKey = NormalizePosCreationKey(request.Order.PosCreationKey);
        request.Order.PosCreationKey = posCreationKey;
        var posFingerprint = posCreationKey is null ? null : Fingerprint(request.Order);
        if (posCreationKey is not null)
        {
            var previous = await FindExistingPosOrderAsync(
                posCreationKey, posFingerprint!, destinationBranch.TenantId, branchId, cancellationToken);
            if (previous is not null) return previous;
        }

        await ValidateAndStampManualBenefitAsync(request.Order, branchId, cancellationToken);

        var order = _mapper.Map<Order>(request.Order);
        var branchTenantId = destinationBranch.TenantId;
        order.TenantId = branchTenantId;
        var originBranchId = _branchContext?.EffectiveBranchId ?? _currentUser.BranchId;
        if (originBranchId <= 0) originBranchId = branchId;
        if (originBranchId != branchId && !await _db.Branches.AsNoTracking().AnyAsync(
                b => b.Id == originBranchId && b.TenantId == branchTenantId, cancellationToken))
            throw new BranchAccessDeniedException();
        order.OriginBranchId = originBranchId;
        order.ManagementVersion = 1;
        order.PosCreationKey = posCreationKey;
        order.PosCreationFingerprint = posFingerprint;
        // Solo las recogidas entre sucursales requieren identificador obligatorio.
        // Mantener opcional el nombre en las recogidas locales existentes.
        if (request.Order.Type == OrderType.Onsite && originBranchId != branchId
            && string.IsNullOrWhiteSpace(request.Order.GuestName))
            throw new BusinessException("Los pedidos para recoger en otra sucursal requieren el nombre de quien recoge");
        if (order.CustomerId.HasValue && !await _db.Customers.AsNoTracking().AnyAsync(
                x => x.Id == order.CustomerId && x.TenantId == branchTenantId && x.Active, cancellationToken))
            throw new BusinessException("El cliente no pertenece al restaurante actual");
        if (order.AddressId.HasValue && !await _db.Addresses.AsNoTracking().AnyAsync(
                x => x.Id == order.AddressId && x.TenantId == branchTenantId && x.CustomerId == order.CustomerId, cancellationToken))
            throw new BusinessException("La dirección no pertenece al cliente o al restaurante actual");
        if (order.AddressId.HasValue)
        {
            var addressService = await _db.AddressBranches.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == branchTenantId && x.AddressId == order.AddressId && x.BranchId == branchId,
                cancellationToken);
            if (addressService is null)
                throw new BusinessException("La dirección no tiene servicio de domicilio configurado para la sucursal seleccionada");
            if (!addressService.IsCovered)
                throw new BusinessException("La dirección está fuera de cobertura para la sucursal seleccionada");
            order.DeliveryFee = addressService.DeliveryFee;
        }
        await ValidatePaymentsForBranchAsync(request.Order, branchId, cancellationToken);
        order.WhatsAppConversationId = request.Order.WhatsAppConversationId;
        if (request.Order.AppliedBenefitType == OrderBenefitType.Manual)
        {
            var grantedByName = await _db.Users.AsNoTracking()
                .Where(x => x.Id == _currentUser.Id).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken);
            order.ManualBenefitReason = request.Order.ManualBenefitReason!.Trim();
            order.ManualBenefitGiftProductId = request.Order.ManualBenefitGiftProductId;
            order.ManualBenefitGrantedByUserId = _currentUser.Id;
            order.ManualBenefitGrantedByUserName = grantedByName;
            order.ManualBenefitGrantedAt = _clock.UtcNow;
        }

        // prepare_at por defecto: reserved_for - 1h si null y hay reserved_for
        if (order.ReservedFor.HasValue && !order.PrepareAt.HasValue)
        {
            order.PrepareAt = order.ReservedFor.Value.AddHours(-1);
        }
        
        // Configurar valores obligatorios inmediatamente después del mapeo
        order.BranchId = branchId;
        order.TakenById = _currentUser.Id;
        order.Status = Domain.Enums.OrderStatus.Taken;
        order.AddStatusTime(Domain.Enums.OrderStatus.Taken, _clock.UtcNow);

        // Mapear y agregar OrderDetails
        if (request.Order.OrderDetails != null && request.Order.OrderDetails.Any())
        {
            var orderDetails = _mapper.Map<List<Domain.Entities.OrderDetail>>(request.Order.OrderDetails);
            foreach (var detail in orderDetails)
            {
               
                order.OrderDetails.Add(detail);
            }

            OrderTotalsHelper.RecalculateFromOrderDetails(order);
        }

        // Every database side effect of order creation is one atomic unit:
        // order, lines, inventory reservations, bank/app payments and cash in
        // store. A 409/validation error must never leave a new order persisted.
        await using var createTransaction = _db.Database.IsRelational()
            && _db.Database.CurrentTransaction is null
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;
        OrderDto result;
        Order fullOrder;
        try
        {
            var createdOrder = await _orderRepository.CreateAsync(order, cancellationToken);
            if (_inventory is not null)
            {
                var deferReservation = createdOrder.Type == OrderType.Reservation
                    && createdOrder.PrepareAt.HasValue
                    && createdOrder.PrepareAt.Value > _clock.UtcNow;
                await _inventory.SnapshotAndReserveAsync(
                    createdOrder, deferReservation, $"order:{createdOrder.Id}:taken", cancellationToken);
            }

            // Batch insert de pagos: un único SaveChangesAsync en lugar de N roundtrips.
            var bankPayments = request.Order.BankPayments?
                .Select(bp => new Domain.Entities.BankPayment
                {
                    OrderId = createdOrder.Id,
                    BankId = bp.BankId,
                    Amount = bp.Amount,
                    IsVerified = false
                }).ToList() ?? [];

            var appPayments = request.Order.AppPayments?
                .Select(ap => new Domain.Entities.AppPayment
                {
                    OrderId = createdOrder.Id,
                    AppId = ap.AppId,
                    Amount = ap.Amount,
                    IsSetted = false
                }).ToList() ?? [];

            if (bankPayments.Count > 0) _db.BankPayments.AddRange(bankPayments);
            if (appPayments.Count > 0) _db.AppPayments.AddRange(appPayments);
            if (bankPayments.Count > 0 || appPayments.Count > 0)
                await _db.SaveChangesAsync(cancellationToken);

            if (request.Order.PaidInStoreCash)
            {
                // order_detail triggers update totals AND bump management_version.
                // EF's tracked creation snapshot is now stale. Refresh original
                // concurrency values after the triggers, before updating cash.
                var refreshed = await _db.ReloadOrderForManagementAsync(createdOrder.Id, cancellationToken);
                if (refreshed is null)
                    throw new BusinessException("Pedido no encontrado tras crear");

                var tracked = await _db.Orders
                    .Include(o => o.BankPayments)
                    .Include(o => o.AppPayments)
                    .FirstOrDefaultAsync(o => o.Id == createdOrder.Id, cancellationToken)
                    ?? throw new BusinessException("Pedido no encontrado tras crear");

                OrderPaidInStoreCashHelper.Apply(
                    tracked, true, _clock.UtcNow, request.Order.PaidInStoreCashAmount);
                await _db.SaveChangesAsync(cancellationToken);
            }

            fullOrder = await _orderRepository.GetByIdWithFullDetailsAsync(createdOrder.Id, cancellationToken)
                ?? throw new BusinessException("Pedido no encontrado");
            result = _mapper.Map<OrderDto>(fullOrder);

            if (createTransaction is not null)
                await createTransaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException) when (posCreationKey is not null)
        {
            if (createTransaction is not null)
                await createTransaction.RollbackAsync(cancellationToken);
            // Covers two simultaneous requests racing on ux_order_pos_creation_tenant:
            // the winner commits, the loser returns that order, never a duplicate.
            var previous = await FindExistingPosOrderAsync(
                posCreationKey, posFingerprint!, destinationBranch.TenantId, branchId, cancellationToken);
            if (previous is not null) return previous;
            throw;
        }
        catch
        {
            if (createTransaction is not null)
                await createTransaction.RollbackAsync(cancellationToken);
            throw;
        }

        // Notificar a cocina: pedido inmediato (sin reserved_for) o prepare_at ya pasó
        var now = _clock.UtcNow;
        var shouldNotifyNow = !fullOrder.ReservedFor.HasValue
            || (fullOrder.PrepareAt.HasValue && fullOrder.PrepareAt.Value <= now);
        if (shouldNotifyNow)
        {
            // Side effects must occur after COMMIT and cannot turn a successfully
            // created order into an error that prompts another manual submission.
            try { await _notificationService.NotifyNewOrderToKitchen(result); }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Order {OrderId} committed but kitchen notification failed", result.Id);
            }

            try { await TryEnqueueKitchenPrintWhenOrderCreatedAsync(fullOrder, cancellationToken); }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Order {OrderId} committed but print settings/read failed", result.Id);
            }
        }

        try { await _notificationService.NotifyOrderManagementChanged(result.Id, result.BranchId, result.OriginBranchId, result.ManagementVersion); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Order created; management invalidation failed for {OrderId}", result.Id); }
        return result;
    }

    private static string? NormalizePosCreationKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!Guid.TryParseExact(raw.Trim(), "D", out var guid) || guid == Guid.Empty)
            throw new BusinessException("La clave de reintento del pedido es inválida.");
        return guid.ToString("D");
    }

    private static string Fingerprint(CreateOrderDto dto) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto))));

    private async Task<OrderDto?> FindExistingPosOrderAsync(
        string key, string fingerprint, int tenantId, int branchId, CancellationToken ct)
    {
        var candidate = await _db.Orders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.PosCreationKey == key)
            .Select(o => new
            {
                o.Id, o.BranchId, o.TakenById, o.PosCreationFingerprint
            })
            .SingleOrDefaultAsync(ct);
        if (candidate is null) return null;

        if (candidate.BranchId != branchId
            || candidate.TakenById != _currentUser.Id
            || candidate.PosCreationFingerprint != fingerprint)
            throw new BusinessException(
                "Este intento de pedido ya existe, pero sus datos cambiaron. Revisa el listado antes de crear otro.");

        var existing = await _orderRepository.GetByIdWithFullDetailsAsync(candidate.Id, ct)
            ?? throw new BusinessException("El pedido ya existe, pero no está disponible para esta sesión.");
        OrderAccessRules.EnsureAccess(existing, _currentUser);
        return _mapper.Map<OrderDto>(existing);
    }

    private async Task ValidatePaymentsForBranchAsync(
        CreateOrderDto dto,
        int branchId,
        CancellationToken cancellationToken)
    {
        var bankIds = (dto.BankPayments ?? []).Select(x => x.BankId).Distinct().ToArray();
        if (bankIds.Length > 0)
        {
            var validBankCount = await _db.Banks.AsNoTracking().CountAsync(
                x => bankIds.Contains(x.Id) && x.BranchId == branchId && x.Active,
                cancellationToken);
            if (validBankCount != bankIds.Length)
                throw new BusinessException("Los bancos del pedido deben estar activos y pertenecer a la sucursal seleccionada");
        }

        var appIds = (dto.AppPayments ?? []).Select(x => x.AppId).Distinct().ToArray();
        if (appIds.Length > 0)
        {
            var validAppCount = await _db.Apps.AsNoTracking().CountAsync(
                x => appIds.Contains(x.Id) && x.Active && x.Bank.Active && x.Bank.BranchId == branchId,
                cancellationToken);
            if (validAppCount != appIds.Length)
                throw new BusinessException("Las apps del pedido deben estar activas y pertenecer a la sucursal seleccionada");
        }
    }

    private async Task TryEnqueueKitchenPrintWhenOrderCreatedAsync(Order order, CancellationToken cancellationToken)
    {
        if (_printQueue is null)
            return;

        var shouldPrint = await _db.BranchPrintSettings.AsNoTracking().AnyAsync(
            s => s.BranchId == order.BranchId
                && s.EnableKitchenJobs
                && s.KitchenAutoPrintTrigger == BranchPrintSettingsEntity.KitchenAutoPrintWhenOrderCreated,
            cancellationToken);
        if (!shouldPrint)
            return;

        try
        {
            await _printQueue.EnqueueAsync(
                order.BranchId,
                PrintJobKind.Kitchen,
                [order.Id],
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "No se encoló comanda de cocina al crear pedido {OrderId} (sucursal {BranchId}). El pedido sí fue creado.",
                order.Id,
                order.BranchId);
        }
    }

    private async Task ValidateAndStampManualBenefitAsync(CreateOrderDto dto, int branchId, CancellationToken ct)
    {
        if (dto.AppliedBenefitType != OrderBenefitType.Manual) return;
        if (!Roles.IsAdminOrSuperadmin(_currentUser.Role))
            throw new BusinessException("Solo administradores pueden conceder beneficios manuales");
        if (string.IsNullOrWhiteSpace(dto.ManualBenefitReason))
            throw new BusinessException("El motivo del beneficio manual es obligatorio");
        if (dto.AppliedBenefitRewardType == LoyaltyRewardType.FreeDelivery)
        {
            if (dto.Type != OrderType.Delivery && !(dto.Type == OrderType.Reservation && dto.AddressId.HasValue))
                throw new BusinessException("El domicilio gratis solo aplica a domicilios o reservas con direccion");
            return;
        }
        if (dto.AppliedBenefitRewardType != LoyaltyRewardType.GiftProduct || !dto.ManualBenefitGiftProductId.HasValue)
            throw new BusinessException("Selecciona un producto regalo valido");
        var validGift = await _db.Products.AsNoTracking().Include(x => x.Category).AnyAsync(x =>
            x.Id == dto.ManualBenefitGiftProductId && x.Active && x.Category.BranchId == branchId &&
            x.Category.Name.ToLower() == "regalos", ct);
        if (!validGift) throw new BusinessException("El producto debe estar activo y pertenecer a la categoria Regalos de la sucursal");
    }
}
