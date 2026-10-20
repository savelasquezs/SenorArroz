using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Domain.Services;

public sealed record EmployeeLeaveEvidenceRule(string Code, string Label, string Stage, bool Required, string Instructions);
public sealed record EmployeeLeaveKind(string Code, string Label, bool PaidByCompanyPolicy,
    string Description = "", EmployeeLeaveEvidenceRule[]? Evidence = null, bool IsPeriodRecord = false,
    string PolicyBasis = "Política interna")
{
    public string Remuneration => PaidByCompanyPolicy ? "paid" : "unpaid";
}
public sealed record EmployeeLeaveEvidenceState(string Code, string Label, string Stage, bool Required,
    string Instructions, string Status);

public static class EmployeeLeaveRules
{
    public const int CurrentPolicyVersion = 2;
    // Classification is the restaurant's policy, not an automatic legal entitlement/payroll calculation.
    // Sources and differences between company policy and statutory rights: docs/employee-leaves-policy.md.
    public static readonly IReadOnlyList<EmployeeLeaveKind> Kinds = Array.AsReadOnly(new[]
    {
        new EmployeeLeaveKind("medical_appointment", "Cita médica", true,
            "Adjunta la confirmación de la cita. Al regresar, entrega la constancia de asistencia, sin diagnóstico ni historia clínica.",
            [Before("appointment", "Confirmación de cita"), After("attendance", "Constancia de asistencia")],
            PolicyBasis: "Política interna; especialistas y urgencias: Ley 2466/2025, art. 15"),
        new EmployeeLeaveKind("medical_emergency", "Urgencia médica propia o familiar", true,
            "No necesitas un archivo para avisar de una urgencia. Entrega después una constancia de atención sin información clínica innecesaria.",
            [After("care", "Constancia de atención")], PolicyBasis: "Urgencia propia: Ley 2466/2025; cobertura familiar: política interna"),
        new EmployeeLeaveKind("bereavement", "Licencia de luto", true,
            "Puedes informar de inmediato y aportar después la acreditación del fallecimiento y parentesco aplicable. Los días hábiles se revisan según la jornada; no se calculan descuentos.",
            [After("bereavement", "Acreditación de fallecimiento y parentesco")], PolicyBasis: "Ley 1280/2009"),
        new EmployeeLeaveKind("domestic_calamity", "Calamidad doméstica", true,
            "Describe lo sucedido sin exponer datos sensibles. No se bloquea el aviso por falta de documentos; presenta una prueba razonable cuando sea posible.",
            [new("event", "Soporte del acontecimiento", "after", false, "Puede ser una constancia o evidencia razonable. No se exige un documento imposible de obtener.")], PolicyBasis: "Ley 2466/2025, art. 15"),
        new EmployeeLeaveKind("authority_summons", "Citación judicial, policial o administrativa", true,
            "Adjunta la citación oficial y, después de asistir, la constancia disponible.",
            [Before("summons", "Citación oficial"), After("attendance", "Constancia de asistencia")], PolicyBasis: "Ley 2466/2025, art. 15"),
        new EmployeeLeaveKind("voting", "Ejercicio del derecho al voto", true,
            "Registra el tiempo necesario para votar y aporta el certificado después. No confundir con el descanso compensatorio electoral.",
            [After("voting", "Certificado de votación")], PolicyBasis: "Ley 2466/2025, art. 15; Ley 403/1997"),
        new EmployeeLeaveKind("lactation", "Registro de período de lactancia", true,
            "Registra una vez el período de pausas, no una solicitud por cada descanso. Este intervalo NO representa ausencia de días completos ni afecta nómina. No se repite el soporte por cada pausa.",
            [new("initial", "Acreditación inicial (si se necesita)", "initial", false, "Solo la información necesaria para organizar las pausas. No adjuntes historia clínica.")],
            IsPeriodRecord: true, PolicyBasis: "Ley 2306/2023, art. 6"),
        new EmployeeLeaveKind("social_security", "Trámites de seguridad social", true,
            "Adjunta el turno si lo tienes y la constancia al terminar. Remuneración según la política interna del restaurante.",
            [new("appointment", "Turno o citación", "before", false, "Adjunta el turno si el trámite es programado."), After("attendance", "Constancia del trámite")]),
        new EmployeeLeaveKind("school_obligation", "Obligación escolar como acudiente", true,
            "Para asistencia obligatoria requerida por el centro educativo. Adjunta el requerimiento, sin información sensible del menor.",
            [Before("school", "Requerimiento del centro educativo"), After("attendance", "Constancia de asistencia")], PolicyBasis: "Ley 2466/2025, art. 15"),
        new EmployeeLeaveKind("mandatory_civic_duty", "Cargo oficial de forzosa aceptación", true,
            "Adjunta la designación oficial y acredita la asistencia después.",
            [Before("designation", "Designación oficial"), After("attendance", "Constancia de cumplimiento")], PolicyBasis: "Ley 2466/2025, art. 15"),
        new EmployeeLeaveKind("marriage", "Matrimonio o unión marital", false,
            "Permiso personal no remunerado por defecto. Puedes adjuntar información del evento; el administrador puede reconocer una condición más favorable.",
            [new("event", "Documento del evento", "after", false, "Opcional, según las circunstancias y la política aplicable.")]),
        new EmployeeLeaveKind("graduation", "Grado académico", false,
            "Indica la fecha de la ceremonia. Adjunta una invitación o certificación si está disponible.",
            [new("event", "Invitación o certificación", "before", false, "Adjunta el documento del evento si lo tienes.")]),
        new EmployeeLeaveKind("personal_errand", "Diligencia personal", false,
            "Explica brevemente la diligencia y el tiempo que necesitas. No se exige soporte por defecto.", []),
        new EmployeeLeaveKind("study", "Capacitación o estudio personal", false,
            "Para estudio por iniciativa personal, no formación obligatoria ordenada por la empresa. Adjunta inscripción, horario o citación.",
            [Before("enrollment", "Inscripción, horario o citación")]),
        new EmployeeLeaveKind("other", "Otro motivo personal", false,
            "Describe el motivo. No utilices esta opción para sustituir un derecho legal o una incapacidad; pide revisión del tipo si corresponde.", [])
    });

    private static EmployeeLeaveEvidenceRule Before(string code, string label) => new(code, label, "before", true,
        "Obligatorio para enviar esta solicitud programada. Adjunta únicamente la información necesaria.");
    private static EmployeeLeaveEvidenceRule After(string code, string label) => new(code, label, "after", true,
        "Se entrega después del acontecimiento; no bloquea el aviso inicial ni produce descuentos automáticos.");
    public static EmployeeLeaveKind Kind(string code) => Kinds.SingleOrDefault(x => x.Code == code)
        ?? throw new BusinessException("Tipo de permiso no válido.");
    public static bool CanReview(User reviewer, EmployeeLeaveRequest request) => reviewer.Active && !reviewer.IsTerminal
        && reviewer.TenantId == request.TenantId && reviewer.Id != request.UserId
        && (reviewer.Role == UserRole.Superadmin || reviewer.Role == UserRole.Admin && reviewer.BranchId == request.BranchId
            && request.ApplicantRole != nameof(UserRole.Admin) && request.ApplicantRole != nameof(UserRole.Superadmin));
    public static bool CanRead(User actor, EmployeeLeaveRequest request) => actor.Active && !actor.IsTerminal
        && actor.TenantId == request.TenantId && (actor.Id == request.UserId || CanReview(actor, request));
    public static bool CanEdit(User actor, EmployeeLeaveRequest request) => actor.Active && !actor.IsTerminal
        && actor.TenantId == request.TenantId && actor.Id == request.UserId && request.Status is "pending" or "postponed";

    public static void ValidateDates(DateTime start, DateTime end, bool isAllDay = false)
    {
        if (start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc || end <= start)
            throw new BusinessException("El fin debe ser posterior al inicio y las fechas deben incluir su zona horaria.");
        if (end - start > TimeSpan.FromDays(366)) throw new BusinessException("El intervalo no puede superar 366 días.");
        if (isAllDay && (start.AddHours(-5).TimeOfDay != TimeSpan.Zero || end.AddHours(-5).TimeOfDay != TimeSpan.Zero))
            throw new BusinessException("Para días completos usa medianoche de Colombia y el día siguiente como fin exclusivo.");
    }
    public static string RequiredText(string? value, int max, string label)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text.Length > max) throw new BusinessException($"{label}: ingresa entre 1 y {max} caracteres.");
        return text;
    }
    public static void ValidateDecision(string status, string kind, bool? isPaid)
    {
        if (status is not ("approved" or "denied" or "postponed")) throw new BusinessException("Decisión no válida.");
        if (status == "approved" && !isPaid.HasValue) throw new BusinessException("Indica la remuneración reconocida.");
        if (status == "approved" && Kind(kind).PaidByCompanyPolicy && isPaid != true)
            throw new BusinessException("Esta categoría es remunerada. No puede aprobarse como no remunerada.");
    }
    public static string EvidenceCode(string kind, string? code)
    {
        code = string.IsNullOrWhiteSpace(code) ? "additional" : code.Trim();
        if (code != "additional" && !(Kind(kind).Evidence ?? []).Any(x => x.Code == code))
            throw new BusinessException("El soporte no corresponde a los requisitos de este tipo de permiso.");
        return code;
    }
    public static EmployeeLeaveEvidenceState[] EvidenceStates(EmployeeLeaveRequest request)
    {
        if (request.PolicyVersion < CurrentPolicyVersion) return [];
        return (Kind(request.Kind).Evidence ?? []).Select(rule =>
        {
            var files = request.Attachments.Where(x => x.EvidenceCode == rule.Code && x.SupersededAt == null).ToArray();
            var status = files.Any(x => x.ReviewStatus == "verified") ? "verified"
                : files.Any(x => x.ReviewStatus == "submitted") ? "submitted"
                : files.Length > 0 ? "rejected" : "missing";
            return new EmployeeLeaveEvidenceState(rule.Code, rule.Label, rule.Stage, rule.Required, rule.Instructions, status);
        }).ToArray();
    }
    public static void RequirePriorEvidence(EmployeeLeaveRequest request)
    {
        var missing = EvidenceStates(request).Where(x => x.Required && x.Stage == "before" && x.Status is "missing" or "rejected").ToArray();
        if (missing.Length > 0) throw new BusinessException("Adjunta un soporte previo válido: " + string.Join(", ", missing.Select(x => x.Label)) + ".");
    }
    public static void RefreshSupport(EmployeeLeaveRequest request)
    {
        var active = request.Attachments.Where(x => x.SupersededAt == null).ToArray();
        var required = EvidenceStates(request).Where(x => x.Required).ToArray();
        request.SupportRequired |= required.Length > 0;
        if (required.Any(x => x.Status == "missing")) request.SupportStatus = "pending";
        else if (required.Any(x => x.Status == "rejected") || active.Any(x => x.ReviewStatus == "rejected")) request.SupportStatus = "rejected";
        else if (active.Length > 0 && active.All(x => x.ReviewStatus == "verified")) request.SupportStatus = "verified";
        else if (active.Length > 0) request.SupportStatus = "submitted";
        else request.SupportStatus = request.SupportRequired ? "pending" : "not_required";
    }
}
