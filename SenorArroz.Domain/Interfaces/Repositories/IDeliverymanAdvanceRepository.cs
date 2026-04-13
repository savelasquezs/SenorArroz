using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IDeliverymanAdvanceRepository
{
    /// <summary>
    /// Obtiene una lista paginada de abonos con filtros opcionales
    /// </summary>
    Task<PagedResult<DeliverymanAdvance>> GetPagedAsync(
        int? deliverymanId = null,
        int? branchId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "createdAt",
        string sortOrder = "desc",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene un abono por su ID
    /// </summary>
    Task<DeliverymanAdvance?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene todos los abonos de un domiciliario en un rango de fechas
    /// </summary>
    Task<IEnumerable<DeliverymanAdvance>> GetByDeliverymanIdAsync(
        int deliverymanId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea un nuevo abono
    /// </summary>
    Task<DeliverymanAdvance> CreateAsync(DeliverymanAdvance advance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Actualiza un abono existente
    /// </summary>
    Task<DeliverymanAdvance> UpdateAsync(DeliverymanAdvance advance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina un abono
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica si un abono existe
    /// </summary>
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el total de abonos de un domiciliario en una fecha específica
    /// </summary>
    Task<decimal> GetTotalAdvancesForDateAsync(int deliverymanId, DateTime date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el total de abonos de un domiciliario en un rango de fechas
    /// </summary>
    Task<decimal> GetTotalAdvancesByDeliverymanAsync(
        int deliverymanId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Total de abonos del domiciliario en el rango del día, excluyendo ciclos anteriores si <paramref name="lastLiquidationAtUtc"/> está definido.
    /// </summary>
    Task<decimal> GetTotalAdvancesForSettlementCycleAsync(
        int deliverymanId,
        DateTime dayFromUtc,
        DateTime dayToUtc,
        DateTime? lastLiquidationAtUtc,
        bool useSettlementCycle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene la cantidad de abonos de un domiciliario en un rango de fechas
    /// </summary>
    Task<int> GetCountByDeliverymanAsync(
        int deliverymanId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>Si ya existe abono por gasto (ExpenseOffset) para este gasto y domiciliario.</summary>
    Task<bool> ExistsExpenseOffsetForExpenseHeaderAsync(
        int deliverymanId,
        int expenseHeaderId,
        CancellationToken cancellationToken = default);
}

