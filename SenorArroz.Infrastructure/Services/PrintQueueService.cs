using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    public PrintQueueService(
        ApplicationDbContext db,
        IOptions<ApiPublicOptions> apiPublic,
        IOptions<BrandingOptions> branding,
        IOrderRepository orderRepository,
        ILoyaltyCycleStepRepository loyaltyCycleStepRepository,
        IClock clock,
        IPrintAgentNotifier printAgentNotifier)
    {
        _db = db;
        _clock = clock;
        var b = apiPublic.Value.BaseUrl?.Trim();
        _publicApiBaseUrl = string.IsNullOrEmpty(b) ? null : b.TrimEnd('/');
        _branding = branding.Value;
        _orderRepository = orderRepository;
        _loyaltyCycleStepRepository = loyaltyCycleStepRepository;
        _printAgentNotifier = printAgentNotifier;
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
        await _printAgentNotifier.NotifyJobsAvailableAsync(branchId, cancellationToken);
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
        await _printAgentNotifier.NotifyJobsAvailableAsync(branchId, cancellationToken);
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
}
