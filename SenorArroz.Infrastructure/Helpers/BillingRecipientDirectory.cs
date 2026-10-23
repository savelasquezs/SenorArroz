using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Infrastructure.Helpers;

public static class BillingRecipientDirectory
{
    public static string Normalize(int type, string identification) => type == 6
        ? ColombianTaxId.Parse(identification.Trim()).Number
        : identification.Trim().ToUpperInvariant();

    public static readonly Expression<Func<BillingRecipientProfile, BillingRecipientDto>> Projection = p => new BillingRecipientDto
    {
        ProfileId = p.Id, ProfileVersion = p.UpdatedAt, DocumentType = p.DocumentType,
        Identification = p.Identification, Name = p.Name, TradeName = p.TradeName,
        Email = p.Email, Address = p.Address, NeedsReview = p.NeedsReview
    };

    public static readonly Expression<Func<CustomerBillingRecipient, BillingRecipientDto>> CustomerProjection = r => new BillingRecipientDto
    {
        Id = r.Id, IsDefault = r.IsDefault, ProfileId = r.ProfileId,
        ProfileVersion = r.Profile == null ? null : r.Profile.UpdatedAt,
        DocumentType = r.Profile == null ? r.DocumentType : r.Profile.DocumentType,
        Identification = r.Profile == null ? r.Identification : r.Profile.Identification,
        Name = r.Profile == null || r.Profile.NeedsReview ? r.Name : r.Profile.Name,
        Email = r.Profile == null || r.Profile.NeedsReview ? r.Email : r.Profile.Email,
        TradeName = r.Profile == null ? null : r.Profile.TradeName,
        Address = r.Profile == null ? null : r.Profile.Address,
        NeedsReview = r.Profile != null && r.Profile.NeedsReview
    };

    public static IQueryable<BillingRecipientProfile> Search(IQueryable<BillingRecipientProfile> query, string search)
    {
        var term = search.Trim();
        var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        return query.Where(x => EF.Functions.ILike(x.Name, pattern)
            || x.TradeName != null && EF.Functions.ILike(x.TradeName, pattern)
            || EF.Functions.ILike(x.Identification, pattern));
    }

    public static async Task<BillingRecipientProfile> SaveAsync(IApplicationDbContext db, BillingRecipientDto dto, CancellationToken ct)
    {
        ElectronicInvoiceService.ValidateRecipient(dto);
        var identification = Normalize(dto.DocumentType, dto.Identification);
        var profile = dto.ProfileId.HasValue
            ? await db.BillingRecipientProfiles.SingleOrDefaultAsync(x => x.Id == dto.ProfileId, ct)
                ?? throw new NotFoundException("El destinatario no está registrado en este negocio.")
            : await db.BillingRecipientProfiles.SingleOrDefaultAsync(x => x.DocumentType == dto.DocumentType && x.Identification == identification, ct);
        if (profile is not null && !dto.ProfileId.HasValue)
        {
            if (profile.NeedsReview || profile.Name != dto.Name.Trim() || profile.Email != Clean(dto.Email)
                || profile.TradeName != Clean(dto.TradeName) || profile.Address != Clean(dto.Address))
                throw new BusinessException("La identificación ya está registrada con otros datos. Busca y selecciona el destinatario del negocio para revisarlo o editarlo.");
            return profile;
        }
        if (profile is not null && (profile.DocumentType != dto.DocumentType || profile.Identification != identification))
            throw new BusinessException("La identificación pertenece a un destinatario compartido. Crea otro para usar una identificación diferente.");
        var email = Clean(dto.Email); var tradeName = Clean(dto.TradeName); var address = Clean(dto.Address);
        if (profile is not null && (profile.Name != dto.Name.Trim() || profile.Email != email || profile.TradeName != tradeName || profile.Address != address || profile.NeedsReview))
        {
            if (!dto.ConfirmSharedUpdate) throw new BusinessException("Confirma el cambio de los datos compartidos del destinatario.");
            if (dto.ProfileVersion != profile.UpdatedAt) throw new BusinessException("Los datos cambiaron en otra pantalla. Busca nuevamente el destinatario antes de editarlo.");
        }
        if (profile is null)
        {
            profile = new BillingRecipientProfile { DocumentType = dto.DocumentType, Identification = identification };
            db.BillingRecipientProfiles.Add(profile);
        }
        profile.Name = dto.Name.Trim(); profile.Email = email; profile.TradeName = tradeName; profile.Address = address; profile.NeedsReview = false;
        await db.SaveChangesAsync(ct);
        return profile;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
