namespace SenorArroz.Application.Features.BranchPrintSettings.DTOs;

public class RotateBranchAgentTokenResponseDto
{
    /// <summary>Guardar en el agente (<c>PrintAgent:AgentToken</c>) y no volver a mostrar.</summary>
    public string PlainToken { get; set; } = string.Empty;
}
