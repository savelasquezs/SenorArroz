using System.Text.RegularExpressions;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public static partial class WhatsAppAiDiagnosticsMapper
{
    public static WhatsAppAiProcessingDto ToDto(
        WhatsAppMessage message,
        int maxAttempts,
        bool includeTechnicalDetail = true,
        DateTime? observedAt = null)
    {
        var technicalDetail = SanitizeTechnicalDetail(message.AiProcessingError);
        var category = ClassifyError(technicalDetail);
        var willRetry = message.AiProcessingStatus == WhatsAppAiProcessingStatus.Pending
            && message.AiProcessingAttempts < maxAttempts;
        var (severity, title, detail) = Describe(message.AiProcessingStatus, technicalDetail, category, willRetry);

        return new WhatsAppAiProcessingDto
        {
            MessageId = message.Id,
            ConversationId = message.ConversationId,
            Status = ToApiStatus(message.AiProcessingStatus),
            Severity = severity,
            Title = title,
            Detail = detail,
            TechnicalDetail = includeTechnicalDetail ? technicalDetail : null,
            ErrorCategory = category,
            HttpStatusCode = ExtractHttpStatusCode(technicalDetail),
            Attempts = message.AiProcessingAttempts,
            MaxAttempts = maxAttempts,
            WillRetry = willRetry,
            Timestamp = AsUtc(message.Timestamp),
            StatusChangedAt = GetStatusChangedAt(message, observedAt),
            StartedAt = AsUtc(message.AiProcessingStartedAt),
            NextRetryAt = AsUtc(message.AiNextRetryAt),
            ProcessedAt = AsUtc(message.AiProcessedAt)
        };
    }

    public static DateTime GetStatusChangedAt(WhatsAppMessage message, DateTime? observedAt = null)
    {
        var now = AsUtc(observedAt ?? DateTime.UtcNow);
        var dueRetryAt = message.AiNextRetryAt.HasValue && AsUtc(message.AiNextRetryAt.Value) <= now
            ? message.AiNextRetryAt
            : null;

        var changedAt = message.AiProcessingStatus switch
        {
            WhatsAppAiProcessingStatus.Processing
                or WhatsAppAiProcessingStatus.ResponseGenerated
                or WhatsAppAiProcessingStatus.Sending
                or WhatsAppAiProcessingStatus.Sent =>
                message.AiProcessingStartedAt ?? message.AiProcessedAt ?? dueRetryAt ?? message.Timestamp,
            WhatsAppAiProcessingStatus.Pending =>
                message.AiProcessedAt ?? dueRetryAt ?? message.Timestamp,
            _ => message.AiProcessedAt ?? message.AiProcessingStartedAt ?? dueRetryAt ?? message.Timestamp
        };

        return AsUtc(changedAt);
    }

    public static string ToApiStatus(WhatsAppAiProcessingStatus status) => status switch
    {
        WhatsAppAiProcessingStatus.NotApplicable => "notApplicable",
        WhatsAppAiProcessingStatus.Pending => "pending",
        WhatsAppAiProcessingStatus.Processing => "processing",
        WhatsAppAiProcessingStatus.ResponseGenerated => "responseGenerated",
        WhatsAppAiProcessingStatus.Sending => "sending",
        WhatsAppAiProcessingStatus.Sent => "sent",
        WhatsAppAiProcessingStatus.Completed => "completed",
        WhatsAppAiProcessingStatus.Ignored => "ignored",
        WhatsAppAiProcessingStatus.Failed => "failed",
        WhatsAppAiProcessingStatus.TransferredToHuman => "transferredToHuman",
        _ => "notApplicable"
    };

    public static string? SanitizeTechnicalDetail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var sanitized = BearerTokenRegex().Replace(value.Trim(), "Bearer [OCULTO]");
        sanitized = QuerySecretRegex().Replace(sanitized, "$1[OCULTO]");
        sanitized = JsonSecretRegex().Replace(sanitized, "$1[OCULTO]$2");
        sanitized = OpenAiKeyRegex().Replace(sanitized, "[OCULTO]");
        sanitized = GeminiKeyRegex().Replace(sanitized, "[OCULTO]");
        sanitized = ApiKeyValueRegex().Replace(sanitized, "$1[OCULTO]");
        return sanitized[..Math.Min(1000, sanitized.Length)];
    }

    public static string? ClassifyError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return null;

        var value = error.ToLowerInvariant();
        if (value == "whatsapp_flow_started") return "flow";
        if (ContainsAny(value, "429", "quota", "cuota", "resource_exhausted", "rate limit", "too many requests", "billing"))
            return "quota";
        if (ContainsAny(value, "401", "403", "api key", "apikey", "unauthenticated", "permission_denied", "credential", "credencial", "unauthorized", "forbidden"))
            return "authentication";
        if (ContainsAny(value, "proveedor no soportado", "provider no soportado", "unsupported provider"))
            return "configuration";
        if (ContainsAny(value, "tipo no soportado", "tipo de mensaje no soportado", "unsupported message"))
            return "unsupported_message";
        if (ContainsAny(value, "conversación inexistente", "conversacion inexistente", "mensaje ya procesado", "orquestador"))
            return "internal";
        if (ContainsAny(value, "model", "modelo", "not found", "no longer available", "unsupported", "no soportado", "chat/completions"))
            return "model";
        if (ContainsAny(value, "timeout", "timed out", "tiempo de espera", "cancelled", "canceled"))
            return "timeout";
        if (ContainsAny(value, "meta", "whatsapp no disponible", "graph api", "envío interrumpido", "envio interrumpido", "post a meta"))
            return "meta";
        if (ContainsAny(value, "ia inactiva", "deshabilitad", "no verificad", "no configurad", "provider no soportado", "proveedor no soportado", "configur"))
            return "configuration";
        if (ContainsAny(value, "herramienta", "tool", "límite del ciclo", "limite del ciclo"))
            return "tools";
        if (ContainsAny(value, "modo human", "modo paused", "modo closed", "modo waiting", "atención cambió", "atencion cambio"))
            return "attention";
        return "provider";
    }

    private static (string Severity, string Title, string Detail) Describe(
        WhatsAppAiProcessingStatus status,
        string? technicalDetail,
        string? category,
        bool willRetry)
    {
        return status switch
        {
            WhatsAppAiProcessingStatus.Ignored when category == "flow" =>
                ("success", "Menú interactivo disponible", "El cliente puede continuar su pedido con los botones de WhatsApp, sin usar IA."),
            WhatsAppAiProcessingStatus.Pending when !string.IsNullOrWhiteSpace(technicalDetail) =>
                ("warning", "Reintento programado", FriendlyError(category, willRetry)),
            WhatsAppAiProcessingStatus.Pending =>
                ("info", "Mensaje en cola", "El mensaje fue recibido y está esperando procesamiento."),
            WhatsAppAiProcessingStatus.Processing =>
                ("info", "IA procesando", "El agente está preparando una respuesta."),
            WhatsAppAiProcessingStatus.ResponseGenerated or WhatsAppAiProcessingStatus.Sending or WhatsAppAiProcessingStatus.Sent =>
                ("info", "Enviando respuesta", "La respuesta fue generada y se está enviando por WhatsApp."),
            WhatsAppAiProcessingStatus.Completed =>
                ("success", "Respondido por IA", "La respuesta fue enviada correctamente."),
            WhatsAppAiProcessingStatus.Failed =>
                ("error", "La IA no pudo responder", FriendlyError(category, false)),
            WhatsAppAiProcessingStatus.TransferredToHuman =>
                ("warning", "Transferido a una persona", category == "meta"
                    ? "La conversación fue transferida, pero no se pudo entregar el aviso al cliente por WhatsApp."
                    : technicalDetail ?? "La conversación requiere atención humana."),
            WhatsAppAiProcessingStatus.Ignored when category is "configuration" =>
                ("error", "Agente de IA no disponible", FriendlyError(category, false)),
            WhatsAppAiProcessingStatus.Ignored =>
                ("neutral", "No se procesó con IA", FriendlyError(category, false)),
            _ =>
                ("neutral", "Sin procesamiento de IA", technicalDetail ?? "Este mensaje no requería procesamiento de IA.")
        };
    }

    private static string FriendlyError(string? category, bool willRetry) => category switch
    {
        "quota" => willRetry
            ? "El proveedor rechazó temporalmente la solicitud por cuota o límite de uso; se volverá a intentar."
            : "El proveedor rechazó la solicitud por cuota o límite de uso.",
        "authentication" => "El proveedor rechazó las credenciales configuradas para la IA.",
        "model" => "El modelo configurado no está disponible o no es compatible con esta operación.",
        "timeout" => willRetry
            ? "El proveedor tardó demasiado en responder; se volverá a intentar."
            : "El procesamiento agotó el tiempo de espera.",
        "meta" => willRetry
            ? "No fue posible completar el envío por WhatsApp; se volverá a intentar cuando sea seguro."
            : "No fue posible confirmar el envío de la respuesta por WhatsApp.",
        "configuration" => "El agente está deshabilitado, no verificado o tiene una configuración inválida.",
        "unsupported_message" => "Este tipo de mensaje no se procesa automáticamente con IA.",
        "internal" => willRetry
            ? "El procesamiento interno falló y se volverá a intentar."
            : "El mensaje no pudo procesarse por un fallo interno.",
        "tools" => "El agente no pudo completar el ciclo de herramientas necesario para responder.",
        "attention" => "La conversación no está asignada actualmente a la IA.",
        _ => willRetry
            ? "El proveedor de IA devolvió un error; se volverá a intentar."
            : "El proveedor de IA devolvió un error y no se pudo enviar una respuesta."
    };

    private static int? ExtractHttpStatusCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = HttpStatusRegex().Match(value);
        return match.Success && int.TryParse(match.Groups[1].Value, out var statusCode)
            ? statusCode
            : null;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(value.Contains);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;

    [GeneratedRegex(@"(?i)Bearer\s+[A-Za-z0-9._~+\-/=]+")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)([?&](?:key|api_key|apikey|access_token)=)[^&\s]+")]
    private static partial Regex QuerySecretRegex();

    [GeneratedRegex("(?i)(\\\"(?:apiKey|api_key|accessToken|access_token|authorization)\\\"\\s*:\\s*\\\")[^\\\"]+(\\\")")]
    private static partial Regex JsonSecretRegex();

    [GeneratedRegex(@"(?i)HTTP\s*(\d{3})")]
    private static partial Regex HttpStatusRegex();

    [GeneratedRegex(@"(?i)\bsk-(?:proj-)?[A-Za-z0-9_-]{8,}")]
    private static partial Regex OpenAiKeyRegex();

    [GeneratedRegex(@"\bAIza[A-Za-z0-9_-]{12,}")]
    private static partial Regex GeminiKeyRegex();

    [GeneratedRegex(@"(?i)(api\s*key(?:\s+provided)?\s*[:=]\s*)[^\s,;""'{}\[\]]+")]
    private static partial Regex ApiKeyValueRegex();
}
