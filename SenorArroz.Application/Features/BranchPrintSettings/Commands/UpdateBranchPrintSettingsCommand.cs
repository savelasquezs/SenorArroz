using MediatR;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.Application.Features.BranchPrintSettings.Commands;

public record UpdateBranchPrintSettingsCommand(int BranchId, UpdateBranchPrintSettingsDto Dto) : IRequest<BranchPrintSettingsDto>;
