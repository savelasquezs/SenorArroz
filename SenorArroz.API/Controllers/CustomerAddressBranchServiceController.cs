using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize]
[Route("api/customers/{customerId:int}/addresses/{addressId:int}/branch-service")]
public sealed class CustomerAddressBranchServiceController(
    IApplicationDbContext db,
    IBranchContext branchContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CustomerAddressBranchServiceDto>>>> Get(
        int customerId,
        int addressId,
        CancellationToken cancellationToken)
    {
        var branchId = branchContext.RequireBranch();
        var tenantId = await ResolveTenantIdAsync(branchId, cancellationToken);
        if (tenantId <= 0)
            return NotFound(ApiResponse<IReadOnlyList<CustomerAddressBranchServiceDto>>.ErrorResponse("Sucursal no encontrada"));

        var addressExists = await db.Addresses.AsNoTracking().AnyAsync(x =>
            x.Id == addressId && x.CustomerId == customerId && x.TenantId == tenantId,
            cancellationToken);
        if (!addressExists)
            return NotFound(ApiResponse<IReadOnlyList<CustomerAddressBranchServiceDto>>.ErrorResponse("Dirección no encontrada"));

        var services = await db.AddressBranches.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AddressId == addressId)
            .OrderBy(x => x.Branch.Name)
            .Select(x => new CustomerAddressBranchServiceDto(
                x.BranchId,
                x.Branch.Name,
                x.DeliveryFee,
                x.IsCovered,
                x.ValidatedAt))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<CustomerAddressBranchServiceDto>>.SuccessResponse(services));
    }

    [HttpPut]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<CustomerAddressBranchServiceDto>>> Upsert(
        int customerId,
        int addressId,
        [FromBody] UpsertCustomerAddressBranchServiceDto request,
        CancellationToken cancellationToken)
    {
        if (request.DeliveryFee < 0)
            return BadRequest(ApiResponse<CustomerAddressBranchServiceDto>.ErrorResponse("El domicilio no puede ser negativo"));

        if (!ValidCoordinates(request.Latitude, request.Longitude))
            return BadRequest(ApiResponse<CustomerAddressBranchServiceDto>.ErrorResponse("Las coordenadas no son válidas"));

        var branchId = branchContext.RequireBranch();
        var branch = await db.Branches.AsNoTracking()
            .Where(x => x.Id == branchId)
            .Select(x => new { x.Id, x.Name, x.TenantId })
            .SingleOrDefaultAsync(cancellationToken);
        if (branch is null)
            return NotFound(ApiResponse<CustomerAddressBranchServiceDto>.ErrorResponse("Sucursal no encontrada"));

        var address = await db.Addresses.FirstOrDefaultAsync(x =>
            x.Id == addressId && x.CustomerId == customerId && x.TenantId == branch.TenantId,
            cancellationToken);
        if (address is null)
            return NotFound(ApiResponse<CustomerAddressBranchServiceDto>.ErrorResponse("Dirección no encontrada"));

        var service = await db.AddressBranches.FirstOrDefaultAsync(x =>
            x.TenantId == branch.TenantId && x.AddressId == addressId && x.BranchId == branch.Id,
            cancellationToken);

        if (service is null)
        {
            service = new AddressBranch
            {
                TenantId = branch.TenantId,
                AddressId = addressId,
                BranchId = branch.Id
            };
            db.AddressBranches.Add(service);
        }

        service.DeliveryFee = request.DeliveryFee;
        service.IsCovered = true;
        service.ValidatedAt = DateTime.UtcNow;

        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            address.Latitude = request.Latitude;
            address.Longitude = request.Longitude;
        }

        await db.SaveChangesAsync(cancellationToken);

        var result = new CustomerAddressBranchServiceDto(
            branch.Id,
            branch.Name,
            service.DeliveryFee,
            service.IsCovered,
            service.ValidatedAt);

        return Ok(ApiResponse<CustomerAddressBranchServiceDto>.SuccessResponse(result, "Domicilio actualizado"));
    }

    private async Task<int> ResolveTenantIdAsync(int branchId, CancellationToken cancellationToken) =>
        await db.Branches.AsNoTracking()
            .Where(x => x.Id == branchId)
            .Select(x => x.TenantId)
            .SingleOrDefaultAsync(cancellationToken);

    private static bool ValidCoordinates(decimal? latitude, decimal? longitude)
    {
        if (!latitude.HasValue && !longitude.HasValue) return true;
        if (!latitude.HasValue || !longitude.HasValue) return false;
        return latitude is >= -90 and <= 90
            && longitude is >= -180 and <= 180
            && !(latitude == 0 && longitude == 0);
    }
}

public sealed record UpsertCustomerAddressBranchServiceDto(
    int DeliveryFee,
    decimal? Latitude = null,
    decimal? Longitude = null);

public sealed record CustomerAddressBranchServiceDto(
    int BranchId,
    string BranchName,
    int DeliveryFee,
    bool IsCovered,
    DateTime? ValidatedAt);
