using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Printing;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models.Printing;

namespace SenorArroz.Infrastructure.Services;

public class PrintQueueService : IPrintQueueService
{
    private readonly ApplicationDbContext _db;

    public PrintQueueService(ApplicationDbContext db)
    {
        _db = db;
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
            .Include(o => o.Branch)
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

        var printedAt = DateTime.UtcNow;
        var batch = PrintTicketPayloadBuilder.BuildBatch(orders, kind, printedAt);
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
        return job;
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
        job.CompletedAt = DateTime.UtcNow;
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
        job.CompletedAt = DateTime.UtcNow;
        job.ErrorMessage = message.Length > 500 ? message[..500] : message;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string KindToDb(PrintJobKind k) => k switch
    {
        PrintJobKind.Kitchen => "kitchen",
        PrintJobKind.Delivery => "delivery",
        PrintJobKind.Cashier => "cashier",
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };
}
