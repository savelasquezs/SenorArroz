using MediatR;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.Application.Features.BranchPrintSettings.Queries;

public record GetPrintAgentConfigQuery(int BranchId) : IRequest<PrintAgentConfigDto?>;
