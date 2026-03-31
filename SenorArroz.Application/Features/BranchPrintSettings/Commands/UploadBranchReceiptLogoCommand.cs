using MediatR;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.Application.Features.BranchPrintSettings.Commands;

public record UploadBranchReceiptLogoCommand(int BranchId, byte[] Content, string ExtensionWithDot) : IRequest<BranchPrintSettingsDto>;
