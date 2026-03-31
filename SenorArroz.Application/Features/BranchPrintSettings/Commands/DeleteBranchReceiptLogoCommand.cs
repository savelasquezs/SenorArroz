using MediatR;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.Application.Features.BranchPrintSettings.Commands;

public record DeleteBranchReceiptLogoCommand(int BranchId) : IRequest<BranchPrintSettingsDto>;
