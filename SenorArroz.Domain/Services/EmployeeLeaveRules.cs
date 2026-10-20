using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Domain.Services;

public sealed record EmployeeLeaveKind(string Code, string Label, bool PaidByCompanyPolicy);

public static class EmployeeLeaveRules
{
    // Catálogo del formato interno. No calcula plazos legales ni descuentos de nómina.
    public static readonly IReadOnlyList<EmployeeLeaveKind> Kinds = Array.AsReadOnly(new[]
    {
        new EmployeeLeaveKind("medical_appointment", "Cita médica", true),
        new EmployeeLeaveKind("medical_emergency", "Urgencia médica propia o familiar", true),
        new EmployeeLeaveKind("bereavement", "Licencia de luto", true),
        new EmployeeLeaveKind("domestic_calamity", "Calamidad doméstica", true),
        new EmployeeLeaveKind("authority_summons", "Citación judicial, policial o administrativa", true),
        new EmployeeLeaveKind("voting", "Ejercicio del derecho al voto", true),
        new EmployeeLeaveKind("lactation", "Pausa de lactancia", true),
        new EmployeeLeaveKind("social_security", "Trámites de seguridad social", true),
        new EmployeeLeaveKind("marriage", "Matrimonio o unión marital", false),
        new EmployeeLeaveKind("graduation", "Grado académico", false),
        new EmployeeLeaveKind("personal_errand", "Diligencia personal", false),
        new EmployeeLeaveKind("study", "Capacitación o estudio", false),
        new EmployeeLeaveKind("other", "Otro", false)
    });

    public static bool CanReview(User reviewer, EmployeeLeaveRequest request) =>
        reviewer.Active && !reviewer.IsTerminal && reviewer.TenantId == request.TenantId
        && reviewer.Id != request.UserId
        && (reviewer.Role == UserRole.Superadmin
            || reviewer.Role == UserRole.Admin && reviewer.BranchId == request.BranchId
                && request.ApplicantRole != nameof(UserRole.Admin)
                && request.ApplicantRole != nameof(UserRole.Superadmin));

    public static bool CanRead(User actor, EmployeeLeaveRequest request) =>
        actor.Active && !actor.IsTerminal && actor.TenantId == request.TenantId
        && (actor.Id == request.UserId || CanReview(actor, request));

    public static bool CanEdit(User actor, EmployeeLeaveRequest request) =>
        actor.Active && !actor.IsTerminal && actor.TenantId == request.TenantId
        && actor.Id == request.UserId && request.Status is "pending" or "postponed";

    public static void ValidateDates(DateTime startsAt, DateTime endsAt)
    {
        if (startsAt.Kind != DateTimeKind.Utc || endsAt.Kind != DateTimeKind.Utc || endsAt <= startsAt)
            throw new BusinessException("Indica fechas UTC válidas; el regreso debe ser posterior a la salida.");
        if (endsAt - startsAt > TimeSpan.FromDays(366))
            throw new BusinessException("El intervalo no puede superar 366 días.");
    }

    public static EmployeeLeaveKind Kind(string code) =>
        Kinds.SingleOrDefault(x => x.Code == code)
        ?? throw new BusinessException("Tipo de permiso no válido.");

    public static string RequiredText(string? value, int max, string label)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text.Length > max)
            throw new BusinessException($"{label}: ingresa entre 1 y {max} caracteres.");
        return text;
    }

    public static void ValidateDecision(string status, string kind, bool? isPaid)
    {
        if (status is not ("approved" or "denied" or "postponed"))
            throw new BusinessException("Decisión no válida.");
        if (status == "approved" && !isPaid.HasValue)
            throw new BusinessException("Indica si el permiso aprobado es remunerado.");
        if (status == "approved" && Kind(kind).PaidByCompanyPolicy && isPaid != true)
            throw new BusinessException("Esta categoría es remunerada en el formato interno. No puede aprobarse como no remunerada.");
    }
}
