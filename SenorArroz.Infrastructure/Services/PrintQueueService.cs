using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Printing;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models.Printing;

namespace SenorArroz.Infrastructure.Services;

public class PrintQueueService : IPrintQueueService
{
    private readonly ApplicationDbContext _db;
    private readonly string? _publicApiBaseUrl;
    private readonly BrandingOptions _branding;
    private readonly IOrderRepository _orderRepository;
    private readonly ILoyaltyCycleStepRepository _loyaltyCycleStepRepository;
    private readonly IClock _clock;
    private readonly IPrintAgentNotifier _printAgentNotifier;
    private readonly ILogger<PrintQueueService> _logger;

    public PrintQueueService(
        ApplicationDbContext db,
        IOptions<ApiPublicOptions> apiPublic,
        IOptions<BrandingOptions> branding,
        IOrderRepository orderRepository,
        ILoyaltyCycleStepRepository loyaltyCycleStepRepository,
        IClock clock,
        IPrintAgentNotifier printAgentNotifier,
        ILogger<PrintQueueService> logger)
    {
        _db = db;
        _clock = clock;
        var b = apiPublic.Value.BaseUrl?.Trim();
        _publicApiBaseUrl = string.IsNullOrEmpty(b) ? null : b.TrimEnd('/');
        _branding = branding.Value;
        _orderRepository = orderRepository;
        _loyaltyCycleStepRepository = loyaltyCycleStepRepository;
        _printAgentNotifier = printAgentNotifier;
        _logger = logger;
    }

    public async Task<bool> IsAgentTokenValidAsync(int branchId, string? plainToken, CancellationToken cancellationToken = default)
    {
        var settings = await _db.BranchPrintSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchId == branchId, cancellationToken);
        if (settings == null) return false;
        return PrintAgentTokenCrypto.IsValid(plainToken, settings);
    }

    public async Task<PrintJob> EnqueueAsync(int branchId, PrintJobKind kind, IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (kind == PrintJobKind.Delivery)
            return await EnqueueDeliveryAsync(branchId, orderIds, null, cancellationToken);

        var totalWatch = Stopwatch.StartNew();
        var validationWatch = Stopwatch.StartNew();
        var settings = await _db.BranchPrintSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchId == branchId, cancellationToken)
            ?? throw new InvalidOperationException("La sucursal no tiene configuración de impresión.");

        var enabled = kind switch
        {
            PrintJobKind.Kitchen => settings.EnableKitchenJobs,
            PrintJobKind.Delivery => settings.EnableDeliveryJobs,
            PrintJobKind.Cashier => settings.EnableCashierJobs,
            _ => false,
        };
        if (!enabled)
            throw new InvalidOperationException("Este tipo de comanda está deshabilitado para la sucursal.");

        var ids = orderIds.Distinct().ToList();
        if (ids.Count == 0)
            throw new InvalidOperationException("Debe indicar al menos un pedido.");
        validationWatch.Stop();

        var queryWatch = Stopwatch.StartNew();
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Branch).ThenInclude(b => b.PrintSettings)
            .Include(o => o.Customer)
            .Include(o => o.Address).ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
            .Include(o => o.BankPayments).ThenInclude(b => b.Bank)
            .Include(o => o.AppPayments).ThenInclude(a => a.App)
            .Where(o => ids.Contains(o.Id))
            .ToListAsync(cancellationToken);
        queryWatch.Stop();

        if (orders.Count != ids.Count)
            throw new InvalidOperationException("Uno o más pedidos no existen.");

        if (orders.Any(o => o.BranchId != branchId))
            throw new InvalidOperationException("Los pedidos deben pertenecer a la sucursal.");

        {
            var noteById = await _db.Orders
                .AsNoTracking()
                .Where(o => ids.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.Notes, cancellationToken);
            foreach (var o in orders)
            {
                if (noteById.TryGetValue(o.Id, out var n))
                    o.Notes = n;
            }
        }

        var loyaltyByOrder = new Dictionary<int, LoyaltyKitchenSnapshot?>();
        foreach (var order in orders)
            loyaltyByOrder[order.Id] = await BuildLoyaltyKitchenSnapshotAsync(order, cancellationToken).ConfigureAwait(false);

        var payloadWatch = Stopwatch.StartNew();
        var printedAt = _clock.UtcNow;
        var restaurantName = string.IsNullOrWhiteSpace(_branding.RestaurantDisplayName)
            ? "El señor arroz"
            : _branding.RestaurantDisplayName.Trim();
        var footer = string.IsNullOrWhiteSpace(_branding.KitchenFooterMessage)
            ? "Gracias por confiar en El señor arroz"
            : _branding.KitchenFooterMessage.Trim();

        var batch = PrintTicketPayloadBuilder.BuildBatch(
            orders,
            kind,
            printedAt,
            _publicApiBaseUrl,
            restaurantName,
            footer,
            loyaltyByOrder);
        var payloadJson = PrintTicketPayloadJson.SerializeBatch(batch);
        var orderIdsJson = JsonSerializer.Serialize(ids.OrderBy(i => i).ToList());
        payloadWatch.Stop();

        var job = new PrintJob
        {
            BranchId = branchId,
            Kind = kind,
            Status = PrintJobStatus.Pending,
            OrderIdsJson = orderIdsJson,
            PayloadJson = payloadJson,
            PayloadVersion = 1,
        };

        _db.PrintJobs.Add(job);
        var saveWatch = Stopwatch.StartNew();
        await _db.SaveChangesAsync(cancellationToken);
        saveWatch.Stop();

        var notificationWatch = Stopwatch.StartNew();
        var notificationSucceeded = true;
        try
        {
            await _printAgentNotifier.NotifyJobsAvailableAsync(branchId, job.Id, kind, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            notificationSucceeded = false;
            _logger.LogWarning(
                ex,
                "Print job persisted but SignalR notification failed. PrintJobId={PrintJobId} BranchId={BranchId} Kind={PrintJobKind}. Polling will recover it.",
                job.Id,
                branchId,
                KindToDb(kind));
        }
        notificationWatch.Stop();
        totalWatch.Stop();

        LogEnqueueTelemetry(
            job,
            validationWatch.Elapsed,
            queryWatch.Elapsed,
            payloadWatch.Elapsed,
            saveWatch.Elapsed,
            notificationWatch.Elapsed,
            totalWatch.Elapsed,
            notificationSucceeded);
        return job;
    }

    public async Task<PrintJob> EnqueueDeliveryAsync(
        int branchId,
        IReadOnlyList<int> orderIds,
        int? deliverymanUserId = null,
        CancellationToken cancellationToken = default)
    {
        var totalWatch = Stopwatch.StartNew();
        var validationWatch = Stopwatch.StartNew();

        var deliveryEnabled = await _db.BranchPrintSettings
            .AsNoTracking()
            .Where(s => s.BranchId == branchId)
            .Select(s => (bool?)s.EnableDeliveryJobs)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("La sucursal no tiene configuración de impresión.");

        if (!deliveryEnabled)
            throw new InvalidOperationException("Este tipo de comanda está deshabilitado para la sucursal.");

        var ids = orderIds.Distinct().ToList();
        if (ids.Count == 0)
            throw new InvalidOperationException("Debe indicar al menos un pedido.");
        validationWatch.Stop();

        var queryWatch = Stopwatch.StartNew();
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new DeliveryPrintSnapshot
            {
                Id = o.Id,
                BranchId = o.BranchId,
                Status = o.Status,
                Type = o.Type,
                DeliveryManId = o.DeliveryManId,
                GuestName = o.GuestName,
                CustomerName = o.Customer == null ? null : o.Customer.Name,
                CustomerPhone1 = o.Customer == null ? null : o.Customer.Phone1,
                CustomerPhone2 = o.Customer == null ? null : o.Customer.Phone2,
                AddressDescription = o.Address == null ? null : o.Address.AddressText,
                AddressAdditionalInfo = o.Address == null ? null : o.Address.AdditionalInfo,
                NeighborhoodName = o.Address == null ? null : o.Address.Neighborhood.Name,
                Subtotal = o.Subtotal,
                DiscountTotal = o.DiscountTotal,
                DeliveryFee = o.DeliveryFee ?? 0,
                Total = o.Total,
                PaidInStoreCash = o.PaidInStoreCash,
                ReservedFor = o.ReservedFor,
                PrepareAt = o.PrepareAt,
                CreatedAt = o.CreatedAt,
                Notes = o.Notes,
                Lines = o.OrderDetails
                    .Select(d => new DeliveryPrintLineSnapshot
                    {
                        Id = d.Id,
                        ProductName = d.Product.Name,
                        Quantity = d.Quantity,
                        UnitPrice = d.UnitPrice,
                        Discount = d.Discount,
                        Subtotal = d.Subtotal,
                        Notes = d.Notes,
                    })
                    .ToList(),
                BankPayments = o.BankPayments
                    .Select(p => new DeliveryPrintBankPaymentSnapshot
                    {
                        BankName = p.Bank.Name,
                        Amount = p.Amount,
                        IsVerified = p.IsVerified,
                    })
                    .ToList(),
                AppPayments = o.AppPayments
                    .Select(p => new DeliveryPrintAppPaymentSnapshot
                    {
                        AppName = p.App.Name,
                        Amount = p.Amount,
                    })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
        queryWatch.Stop();

        if (orders.Count != ids.Count)
            throw new InvalidOperationException("Uno o más pedidos no existen.");
        if (orders.Any(o => o.BranchId != branchId))
            throw new InvalidOperationException("Los pedidos deben pertenecer a la sucursal.");

        if (deliverymanUserId.HasValue)
        {
            foreach (var order in orders)
            {
                if (order.Type != OrderType.Delivery)
                    throw new InvalidOperationException("Solo se pueden imprimir pedidos de domicilio.");
                if (order.Status != OrderStatus.OnTheWay)
                    throw new InvalidOperationException("El pedido debe estar en ruta (en camino).");
                if (order.DeliveryManId != deliverymanUserId)
                    throw new InvalidOperationException("Solo puedes imprimir pedidos asignados a ti.");
            }
        }

        var payloadWatch = Stopwatch.StartNew();
        var batch = DeliveryPrintPayloadBuilder.BuildBatch(orders, _clock.UtcNow);
        var payloadJson = PrintTicketPayloadJson.SerializeBatch(batch);
        var orderIdsJson = JsonSerializer.Serialize(ids.OrderBy(i => i).ToList());
        payloadWatch.Stop();

        var job = new PrintJob
        {
            BranchId = branchId,
            Kind = PrintJobKind.Delivery,
            Status = PrintJobStatus.Pending,
            OrderIdsJson = orderIdsJson,
            PayloadJson = payloadJson,
            PayloadVersion = 1,
        };

        _db.PrintJobs.Add(job);
        var saveWatch = Stopwatch.StartNew();
        await _db.SaveChangesAsync(cancellationToken);
        saveWatch.Stop();

        var notificationWatch = Stopwatch.StartNew();
        var notificationSucceeded = true;
        try
        {
            await _printAgentNotifier.NotifyJobsAvailableAsync(
                branchId,
                job.Id,
                PrintJobKind.Delivery,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            notificationSucceeded = false;
            _logger.LogWarning(
                ex,
                "Delivery print job persisted but SignalR notification failed. PrintJobId={PrintJobId} BranchId={BranchId}. Polling will recover it.",
                job.Id,
                branchId);
        }
        notificationWatch.Stop();
        totalWatch.Stop();

        LogEnqueueTelemetry(
            job,
            validationWatch.Elapsed,
            queryWatch.Elapsed,
            payloadWatch.Elapsed,
            saveWatch.Elapsed,
            notificationWatch.Elapsed,
            totalWatch.Elapsed,
            notificationSucceeded);
        return job;
    }

    public async Task<PrintJob> EnqueueTestPrintAsync(int branchId, PrintJobKind kind, CancellationToken cancellationToken = default)
    {
        if (kind is not PrintJobKind.Kitchen and not PrintJobKind.Delivery)
            throw new InvalidOperationException("Solo se admite comanda de cocina o domicilio.");

        var branch = await _db.Branches.AsNoTracking()
                .Include(b => b.PrintSettings)
                .FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken)
            ?? throw new InvalidOperationException("Sucursal no encontrada.");

        var settings = branch.PrintSettings
            ?? throw new InvalidOperationException("La sucursal no tiene configuración de impresión.");

        var enabled = kind switch
        {
            PrintJobKind.Kitchen => settings.EnableKitchenJobs,
            PrintJobKind.Delivery => settings.EnableDeliveryJobs,
            _ => false,
        };
        if (!enabled)
            throw new InvalidOperationException("Este tipo de comanda está deshabilitado para la sucursal.");

        var printedAt = _clock.UtcNow;
        var restaurantName = string.IsNullOrWhiteSpace(_branding.RestaurantDisplayName)
            ? "El señor arroz"
            : _branding.RestaurantDisplayName.Trim();
        var footer = string.IsNullOrWhiteSpace(_branding.KitchenFooterMessage)
            ? "Gracias por confiar en El señor arroz"
            : _branding.KitchenFooterMessage.Trim();

        var batch = PrintTicketPayloadBuilder.BuildTestBatch(
            branch,
            kind,
            printedAt,
            _publicApiBaseUrl,
            restaurantName,
            footer);
        var payloadJson = PrintTicketPayloadJson.SerializeBatch(batch);
        var orderIdsJson = JsonSerializer.Serialize(new[] { PrintTicketPayloadBuilder.TestPrintSyntheticOrderId });

        var job = new PrintJob
        {
            BranchId = branchId,
            Kind = kind,
            Status = PrintJobStatus.Pending,
            OrderIdsJson = orderIdsJson,
            PayloadJson = payloadJson,
            PayloadVersion = 1,
        };

        _db.PrintJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            await _printAgentNotifier.NotifyJobsAvailableAsync(branchId, job.Id, kind, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Test print job persisted but SignalR notification failed. PrintJobId={PrintJobId} BranchId={BranchId} Kind={PrintJobKind}.",
                job.Id,
                branchId,
                KindToDb(kind));
        }
        return job;
    }

    private async Task<LoyaltyKitchenSnapshot?> BuildLoyaltyKitchenSnapshotAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.CustomerId is not int cid)
            return null;

        var delivered = await _orderRepository.CountDeliveredOrdersForCustomerAsync(cid, cancellationToken).ConfigureAwait(false);
        var cycleLen = await _loyaltyCycleStepRepository.GetCycleLengthAsync(order.BranchId, cancellationToken).ConfigureAwait(false);

        int? until = null;
        string? nextLabel = null;
        if (cycleLen > 0)
        {
            until = LoyaltyDeliveriesPerReward.GetDeliveriesUntilNextReward(delivered);
            var nextMilestone = LoyaltyDeliveriesPerReward.GetNextRewardMilestoneDeliveries(delivered);
            var nextStepIndex = LoyaltyDeliveriesPerReward.GetStepIndexAtMilestone(nextMilestone, cycleLen);
            var step = await _loyaltyCycleStepRepository
                .GetByBranchAndStepIndexAsync(order.BranchId, nextStepIndex, cancellationToken)
                .ConfigureAwait(false);
            nextLabel = string.IsNullOrWhiteSpace(step?.RewardLabel) ? null : step!.RewardLabel.Trim();
        }

        var gift = !string.IsNullOrWhiteSpace(order.LoyaltyRewardSnapshot)
            ? order.LoyaltyRewardSnapshot.Trim()
            : (string.IsNullOrWhiteSpace(order.LoyaltyCycleStep?.RewardLabel)
                ? null
                : order.LoyaltyCycleStep!.RewardLabel.Trim());

        return new LoyaltyKitchenSnapshot(delivered, until, nextLabel, gift);
    }

    public async Task ValidateDeliverymanDeliveryEnqueueAsync(
        int branchId,
        int deliverymanUserId,
        IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        var ids = orderIds.Distinct().ToList();
        if (ids.Count == 0)
            throw new InvalidOperationException("Debe indicar al menos un pedido.");

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.BranchId, o.Status, o.Type, o.DeliveryManId })
            .ToListAsync(cancellationToken);

        if (orders.Count != ids.Count)
            throw new InvalidOperationException("Uno o más pedidos no existen.");

        foreach (var o in orders)
        {
            if (o.BranchId != branchId)
                throw new InvalidOperationException("Los pedidos deben pertenecer a la sucursal.");
            if (o.Type != OrderType.Delivery)
                throw new InvalidOperationException("Solo se pueden imprimir pedidos de domicilio.");
            if (o.Status != OrderStatus.OnTheWay)
                throw new InvalidOperationException("El pedido debe estar en ruta (en camino).");
            if (o.DeliveryManId != deliverymanUserId)
                throw new InvalidOperationException("Solo puedes imprimir pedidos asignados a ti.");
        }
    }

    public async Task<IReadOnlyList<PrintJobAgentItemDto>> ClaimPendingForAgentAsync(
        int branchId,
        IReadOnlyList<PrintJobKind> kinds,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (kinds.Count == 0 || take <= 0)
            return Array.Empty<PrintJobAgentItemDto>();

        take = Math.Clamp(take, 1, 50);
        var kindStrings = kinds.Select(KindToDb).ToArray();

        var previousTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(120);
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            if (tx is not RelationalTransaction relationalTx
                || relationalTx.GetDbTransaction() is not NpgsqlTransaction npgsqlTx)
                throw new InvalidOperationException("Se requiere Npgsql como proveedor de base de datos.");
            cmd.Transaction = npgsqlTx;
            cmd.CommandText = """
                WITH cte AS (
                  SELECT id FROM print_job
                  WHERE branch_id = @branch_id AND status = 'pending' AND kind = ANY(@kinds)
                  ORDER BY created_at
                  FOR UPDATE SKIP LOCKED
                  LIMIT @take
                )
                UPDATE print_job AS pj
                SET status = 'processing', started_at = NOW()
                FROM cte
                WHERE pj.id = cte.id
                RETURNING pj.id, pj.kind, pj.order_ids_json::text, pj.payload_json::text, pj.payload_version
                """;
            cmd.Parameters.Add(new NpgsqlParameter("branch_id", branchId));
            cmd.Parameters.Add(new NpgsqlParameter("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = kindStrings });
            cmd.Parameters.Add(new NpgsqlParameter("take", take));

            var list = new List<PrintJobAgentItemDto>();
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    list.Add(new PrintJobAgentItemDto(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetInt16(4)));
                }
            }

            await tx.CommitAsync(cancellationToken);
            return list;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            _db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    public async Task<PrintJobAgentItemDto?> ClaimSpecificForAgentAsync(
        int branchId,
        long jobId,
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var conn = _db.Database.GetDbConnection();
        var shouldClose = conn.State != ConnectionState.Open;
        if (shouldClose)
            await conn.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE print_job
                SET status = 'processing', started_at = NOW()
                WHERE id = @job_id
                  AND branch_id = @branch_id
                  AND status = 'pending'
                RETURNING id, kind, order_ids_json::text, payload_json::text, payload_version
                """;
            cmd.Parameters.Add(new NpgsqlParameter("job_id", jobId));
            cmd.Parameters.Add(new NpgsqlParameter("branch_id", branchId));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return new PrintJobAgentItemDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt16(4));
        }
        finally
        {
            watch.Stop();
            _logger.LogInformation(
                "Specific print job claim finished. PrintJobId={PrintJobId} BranchId={BranchId} ClaimElapsedMs={ClaimElapsedMs}.",
                jobId,
                branchId,
                watch.Elapsed.TotalMilliseconds);
            if (shouldClose)
                await conn.CloseAsync();
        }
    }

    public async Task<PrintJobStatusDto?> GetJobStatusAsync(
        int branchId,
        long jobId,
        int? deliverymanUserId = null,
        CancellationToken cancellationToken = default)
    {
        var job = await _db.PrintJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId && j.BranchId == branchId)
            .Select(j => new
            {
                j.Id,
                j.Status,
                j.Kind,
                j.CreatedAt,
                j.StartedAt,
                j.CompletedAt,
                j.ErrorMessage,
                j.OrderIdsJson,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
            return null;

        if (deliverymanUserId.HasValue)
        {
            if (job.Kind != PrintJobKind.Delivery)
                return null;

            List<int>? orderIds;
            try
            {
                orderIds = JsonSerializer.Deserialize<List<int>>(job.OrderIdsJson);
            }
            catch (JsonException)
            {
                return null;
            }

            if (orderIds is null || orderIds.Count == 0)
                return null;

            var assignedCount = await _db.Orders
                .AsNoTracking()
                .CountAsync(
                    o => orderIds.Contains(o.Id)
                        && o.BranchId == branchId
                        && o.Type == OrderType.Delivery
                        && o.DeliveryManId == deliverymanUserId.Value,
                    cancellationToken);
            if (assignedCount != orderIds.Distinct().Count())
                return null;
        }

        return new PrintJobStatusDto(
            job.Id,
            StatusToDb(job.Status),
            KindToDb(job.Kind),
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.ErrorMessage);
    }

    public async Task<bool> TryCompleteJobAsync(int branchId, long jobId, CancellationToken cancellationToken = default)
    {
        var job = await _db.PrintJobs.FirstOrDefaultAsync(
            j => j.Id == jobId && j.BranchId == branchId,
            cancellationToken);
        if (job is null || job.Status != PrintJobStatus.Processing)
            return false;

        job.Status = PrintJobStatus.Done;
        job.CompletedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryFailJobAsync(int branchId, long jobId, string message, CancellationToken cancellationToken = default)
    {
        var job = await _db.PrintJobs.FirstOrDefaultAsync(
            j => j.Id == jobId && j.BranchId == branchId,
            cancellationToken);
        if (job is null || job.Status != PrintJobStatus.Processing)
            return false;

        job.Status = PrintJobStatus.Failed;
        job.CompletedAt = _clock.UtcNow;
        job.ErrorMessage = message.Length > 500 ? message[..500] : message;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string KindToDb(PrintJobKind k) => k switch
    {
        PrintJobKind.Kitchen => Roles.Kitchen,
        PrintJobKind.Delivery => "delivery",
        PrintJobKind.Cashier => Roles.Cashier,
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };

    private static string StatusToDb(PrintJobStatus status) => status switch
    {
        PrintJobStatus.Pending => "pending",
        PrintJobStatus.Processing => "processing",
        PrintJobStatus.Done => "done",
        PrintJobStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private void LogEnqueueTelemetry(
        PrintJob job,
        TimeSpan validation,
        TimeSpan orderQuery,
        TimeSpan payload,
        TimeSpan save,
        TimeSpan notification,
        TimeSpan total,
        bool notificationSucceeded)
    {
        _logger.LogInformation(
            "Print job enqueued. PrintJobId={PrintJobId} BranchId={BranchId} Kind={PrintJobKind} ValidationElapsedMs={ValidationElapsedMs} OrderQueryElapsedMs={OrderQueryElapsedMs} PayloadElapsedMs={PayloadElapsedMs} SaveElapsedMs={SaveElapsedMs} SignalRElapsedMs={SignalRElapsedMs} TotalElapsedMs={TotalElapsedMs} SignalRSucceeded={SignalRSucceeded}.",
            job.Id,
            job.BranchId,
            KindToDb(job.Kind),
            validation.TotalMilliseconds,
            orderQuery.TotalMilliseconds,
            payload.TotalMilliseconds,
            save.TotalMilliseconds,
            notification.TotalMilliseconds,
            total.TotalMilliseconds,
            notificationSucceeded);
    }
}
