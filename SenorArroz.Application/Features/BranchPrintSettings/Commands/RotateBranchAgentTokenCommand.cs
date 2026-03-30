using MediatR;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.Application.Features.BranchPrintSettings.Commands;

public record RotateBranchAgentTokenCommand(int BranchId) : IRequest<RotateBranchAgentTokenResponseDto>;
