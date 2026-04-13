using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Commands;

public class UpdateBranchHandler : IRequestHandler<UpdateBranchCommand, BranchDto>
{
    private readonly IBranchRepository _branchRepository;
    private readonly IApplicationDbContext _db;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public UpdateBranchHandler(IBranchRepository branchRepository, IApplicationDbContext db, IMapper mapper, ICurrentUser currentUser)
    {
        _branchRepository = branchRepository;
        _db = db;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<BranchDto> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        var branch = await _branchRepository.GetByIdAsync(request.Id, cancellationToken);
        if (branch == null)
        {
            throw new NotFoundException($"Sucursal con ID {request.Id} no encontrada");
        }

        var role = _currentUser.Role ?? string.Empty;
        var isSuperadmin = string.Equals(role, "superadmin", StringComparison.OrdinalIgnoreCase);
        var isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);

        if (!isSuperadmin && isAdmin)
        {
            if (request.Id != _currentUser.BranchId)
                throw new BusinessException("No puedes editar una sucursal que no sea la tuya.");

            if (!string.Equals(request.Name.Trim(), branch.Name, StringComparison.Ordinal))
                throw new BusinessException("Solo un superadmin puede cambiar el nombre de la sucursal.");
        }

        // Validate name doesn't exist for other branches
        if (await _branchRepository.NameExistsAsync(request.Name, request.Id))
        {
            throw new BusinessException($"Ya existe otra sucursal con el nombre '{request.Name}'");
        }

        // Validate phone doesn't exist for other branches
        if (await _branchRepository.PhoneExistsAsync(request.Phone1, request.Id))
        {
            throw new BusinessException($"Ya existe otra sucursal con el teléfono {request.Phone1}");
        }

        if (!string.IsNullOrEmpty(request.Phone2) && await _branchRepository.PhoneExistsAsync(request.Phone2, request.Id))
        {
            throw new BusinessException($"Ya existe otra sucursal con el teléfono {request.Phone2}");
        }

        BranchCoordinatesValidator.EnsureValid(request.Latitude, request.Longitude);

        // Update branch
        branch.Name = request.Name.Trim();
        branch.BusinessName = NullIfWhiteSpace(request.BusinessName);
        branch.Nit = NullIfWhiteSpace(request.Nit);
        branch.Address = request.Address.Trim();
        branch.Phone1 = request.Phone1;
        branch.Phone2 = request.Phone2;
        branch.Latitude = request.Latitude;
        branch.Longitude = request.Longitude;

        branch = await _branchRepository.UpdateAsync(branch, cancellationToken);

        var branchDto = _mapper.Map<BranchDto>(branch);

        // Add current statistics
        branchDto.TotalUsers = await _branchRepository.GetTotalUsersAsync(branch.Id, cancellationToken);
        branchDto.ActiveUsers = await _branchRepository.GetActiveUsersAsync(branch.Id, cancellationToken);
        branchDto.TotalCustomers = await _branchRepository.GetTotalCustomersAsync(branch.Id, cancellationToken);
        branchDto.ActiveCustomers = await _branchRepository.GetActiveCustomersAsync(branch.Id, cancellationToken);
        branchDto.TotalNeighborhoods = await _branchRepository.GetTotalNeighborhoodsAsync(branch.Id, cancellationToken);

        var ps = await _db.BranchPrintSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchId == branch.Id, cancellationToken);
        branchDto.PrintSettings = ps is null ? null : _mapper.Map<BranchPrintSettingsDto>(ps);

        return branchDto;
    }

    private static string? NullIfWhiteSpace(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
