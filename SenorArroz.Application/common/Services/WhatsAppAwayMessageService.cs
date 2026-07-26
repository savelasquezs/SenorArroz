using System.Text.RegularExpressions;
using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Application.Common.Services;

public partial class WhatsAppAwayMessageService
{
    public const int MaxTemplateLength = 3500;
    public const string DefaultTemplate = "¡Hola! Gracias por escribir a {{BranchName}}. En este momento estamos fuera de nuestro horario de atención. Volvemos a atender {{NextOpening}}. Tu mensaje quedó registrado y lo revisaremos cuando abramos.";

    private static readonly HashSet<string> AllowedVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "BranchName",
        "NextOpening"
    };

    public string? ValidateTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return "El mensaje de ausencia es requerido cuando la función está activa.";
        if (template.Length > MaxTemplateLength)
            return $"El mensaje de ausencia no puede superar {MaxTemplateLength} caracteres.";

        var unknown = VariableRegex().Matches(template)
            .Select(match => match.Groups[1].Value)
            .FirstOrDefault(variable => !AllowedVariables.Contains(variable));
        if (unknown is not null)
            return $"La variable {{{{{unknown}}}}} no está disponible en el mensaje de ausencia.";

        var withoutKnownVariables = VariableRegex().Replace(template, string.Empty);
        if (withoutKnownVariables.Contains("{{", StringComparison.Ordinal)
            || withoutKnownVariables.Contains("}}", StringComparison.Ordinal))
        {
            return "El mensaje de ausencia contiene una variable incompleta.";
        }

        return null;
    }

    public string Render(string template, string branchName, DateTime nowUtc, DateTime nextOpeningAtUtc)
    {
        var validationError = ValidateTemplate(template);
        if (validationError is not null)
            throw new ArgumentException(validationError, nameof(template));

        var nextOpening = FormatNextOpening(nowUtc, nextOpeningAtUtc);
        return VariableRegex().Replace(template, match =>
            match.Groups[1].Value.Equals("BranchName", StringComparison.OrdinalIgnoreCase)
                ? branchName
                : nextOpening);
    }

    public static string BuildDispatchKey(int conversationId, DateTime closedPeriodStartedAtUtc) =>
        $"away:{conversationId}:{closedPeriodStartedAtUtc:yyyyMMddHHmmss}";

    public static string FormatNextOpening(DateTime nowUtc, DateTime nextOpeningAtUtc)
    {
        var now = ColombiaTimeHelper.GetNowInColombiaFromUtc(nowUtc);
        var next = ColombiaTimeHelper.GetNowInColombiaFromUtc(nextOpeningAtUtc);
        var dayText = next.Date == now.Date
            ? "hoy"
            : next.Date == now.Date.AddDays(1)
                ? "mañana"
                : $"el {DayName(next.DayOfWeek)}";
        return $"{dayText} a las {FormatTime(next)}";
    }

    private static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "lunes",
        DayOfWeek.Tuesday => "martes",
        DayOfWeek.Wednesday => "miércoles",
        DayOfWeek.Thursday => "jueves",
        DayOfWeek.Friday => "viernes",
        DayOfWeek.Saturday => "sábado",
        _ => "domingo"
    };

    private static string FormatTime(DateTime value)
    {
        var hour = value.Hour % 12;
        if (hour == 0)
            hour = 12;
        var period = value.Hour < 12 ? "a. m." : "p. m.";
        return $"{hour}:{value.Minute:00} {period}";
    }

    [GeneratedRegex(@"{{\s*([a-zA-Z0-9_]+)\s*}}", RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();
}
