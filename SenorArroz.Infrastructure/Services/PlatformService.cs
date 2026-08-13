using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Saas.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Constants;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed partial class PlatformService : IPlatformService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantExecutionContext _executionContext;
    private readonly IPlatformCurrentUser _currentUser;
    private readonly IPasswordService _passwords;
    private readonly IEmailService _email;
    private readonly ITenantConnectionRegistry _connections;
    private readonly string _invitationUrl;

    public PlatformService(
        ApplicationDbContext context,
        ITenantExecutionContext executionContext,
        IPlatformCurrentUser currentUser,
        IPasswordService passwords,
        IEmailService email,
        ITenantConnectionRegistry connections,
        IConfiguration configuration)
    {
        _context = context;
        _executionContext = executionContext;
        _currentUser = currentUser;
        _passwords = passwords;
        _email = email;
        _connections = connections;
        _invitationUrl = configuration["FrontendSettings:TenantInvitationUrl"] ?? "https://senorarroz.com/accept-invitation";
    }

    public async Task<IReadOnlyList<PlatformTenantListItemDto>> GetTenantsAsync(string? search, string? status, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var query = _context.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.DisplayName.ToLower().Contains(term) || x.Slug.ToLower().Contains(term) || x.ContactEmail.ToLower().Contains(term));
        }
        if (Enum.TryParse<TenantStatus>(status, true, out var tenantStatus)) query = query.Where(x => x.Status == tenantStatus);

        return await query.OrderBy(x => x.DisplayName).Select(x => new PlatformTenantListItemDto(
            x.Id, x.PublicId, x.DisplayName, x.Slug, x.Status.ToString().ToLower(), x.ContactEmail,
            x.Subscriptions.Where(s => s.Status == TenantSubscriptionStatus.Active).Select(s => s.PlanVersion.Plan.Name).FirstOrDefault() ?? "Sin plan",
            x.Branches.Count, x.Users.Count, x.CreatedAt)).ToListAsync(cancellationToken);
    }

    public async Task<PlatformTenantDetailDto> GetTenantAsync(int id, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        return await LoadTenantDetailAsync(id, cancellationToken);
    }

    public async Task<PlatformTenantDetailDto> CreateTenantAsync(CreatePlatformTenantRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        ValidateSlug(request.Slug);
        var slug = request.Slug.Trim().ToLowerInvariant();
        var email = request.AdminEmail.Trim().ToLowerInvariant();
        if (await _context.Tenants.AnyAsync(x => x.Slug == slug, cancellationToken)) throw new InvalidOperationException("El slug ya está registrado.");
        if (await _context.Users.AnyAsync(x => x.Email == email, cancellationToken)) throw new InvalidOperationException("El correo del administrador ya está registrado.");
        var version = await _context.SaasPlanVersions.Include(x => x.Plan).SingleOrDefaultAsync(x => x.Id == request.PlanVersionId && x.Status == PlanVersionStatus.Published, cancellationToken)
            ?? throw new InvalidOperationException("La versión del plan no está publicada.");

        await using var transaction = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            DisplayName = request.Name.Trim(), Slug = slug, ContactName = request.ContactName.Trim(), ContactEmail = request.ContactEmail.Trim().ToLowerInvariant(),
            ContactPhone = Clean(request.ContactPhone), LegalName = Clean(request.LegalName), TaxId = Clean(request.TaxId), BillingAddress = Clean(request.BillingAddress),
            Status = TenantStatus.Draft, CreatedAt = now, UpdatedAt = now
        };
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync(cancellationToken);

        var branch = new Branch
        {
            TenantId = tenant.Id, Name = request.BranchName.Trim(), Address = request.BranchAddress.Trim(), Phone1 = request.BranchPhone.Trim()
        };
        _context.Branches.Add(branch);
        await _context.SaveChangesAsync(cancellationToken);

        var user = new User
        {
            TenantId = tenant.Id, BranchId = branch.Id, Name = request.AdminName.Trim(), Email = email, Phone = request.AdminPhone.Trim(),
            Role = UserRole.Superadmin, Active = false, PasswordHash = _passwords.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
        };
        _context.Users.Add(user);
        _context.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id, PlanVersionId = version.Id, Status = TenantSubscriptionStatus.Active, StartsAt = now, CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var addon in await _context.SaasAddons.Where(x => request.Addons.Contains(x.Code) && x.Active).ToListAsync(cancellationToken))
            _context.TenantAddons.Add(new TenantAddon { TenantId = tenant.Id, AddonId = addon.Id, Active = true, EnabledAt = now, UpdatedAt = now });

        var invitation = await CreateInvitationAsync(tenant, branch, user, cancellationToken);
        await AuditAsync("tenant.create", nameof(Tenant), tenant.Id.ToString(), null, tenant, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await SendInvitationAsync(invitation.Invitation, invitation.Token, user.Name, tenant.DisplayName);
        return await LoadTenantDetailAsync(tenant.Id, cancellationToken);
    }

    public async Task<PlatformTenantDetailDto> UpdateTenantAsync(int id, UpdatePlatformTenantRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        ValidateSlug(request.Slug);
        var tenant = await _context.Tenants.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Cliente no encontrado.");
        var before = Snapshot(tenant);
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await _context.Tenants.AnyAsync(x => x.Id != id && x.Slug == slug, cancellationToken)) throw new InvalidOperationException("El slug ya está registrado.");
        tenant.DisplayName = request.Name.Trim(); tenant.Slug = slug; tenant.ContactName = request.ContactName.Trim();
        tenant.ContactEmail = request.ContactEmail.Trim().ToLowerInvariant(); tenant.ContactPhone = Clean(request.ContactPhone);
        tenant.LegalName = Clean(request.LegalName); tenant.TaxId = Clean(request.TaxId); tenant.BillingAddress = Clean(request.BillingAddress); tenant.UpdatedAt = DateTime.UtcNow;
        await AuditAsync("tenant.update", nameof(Tenant), id.ToString(), before, tenant, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return await LoadTenantDetailAsync(id, cancellationToken);
    }

    public async Task<PlatformTenantDetailDto> ChangeTenantStatusAsync(int id, ChangeTenantStatusRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        if (!Enum.TryParse<TenantStatus>(request.Status, true, out var status)) throw new InvalidOperationException("Estado de tenant inválido.");
        var tenant = await _context.Tenants.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Cliente no encontrado.");
        var before = Snapshot(tenant);
        tenant.Status = status; tenant.StatusReason = Clean(request.Reason); tenant.UpdatedAt = DateTime.UtcNow; tenant.AccessVersion++;
        tenant.SuspendedAt = status == TenantStatus.Suspended ? DateTime.UtcNow : null;
        tenant.CancelledAt = status == TenantStatus.Cancelled ? DateTime.UtcNow : null;
        if (status is TenantStatus.Suspended or TenantStatus.Cancelled)
        {
            var tokens = await _context.RefreshTokens.Where(x => x.TenantId == id && !x.IsRevoked).ToListAsync(cancellationToken);
            foreach (var token in tokens) token.Revoke(requestContext.IpAddress, DateTime.UtcNow);
        }
        await AuditAsync("tenant.status", nameof(Tenant), id.ToString(), before, tenant, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        if (status is TenantStatus.Suspended or TenantStatus.Cancelled) _connections.Revoke(id);
        return await LoadTenantDetailAsync(id, cancellationToken);
    }

    public async Task<PlatformTenantDetailDto> ChangeSubscriptionAsync(int id, ChangeTenantSubscriptionRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var version = await _context.SaasPlanVersions.Include(x => x.Modules).ThenInclude(x => x.Module)
            .SingleOrDefaultAsync(x => x.Id == request.PlanVersionId && x.Status == PlanVersionStatus.Published, cancellationToken)
            ?? throw new InvalidOperationException("La versión del plan no está publicada.");
        var branchCount = await _context.Branches.CountAsync(x => x.TenantId == id, cancellationToken);
        var userCount = await _context.Users.CountAsync(x => x.TenantId == id, cancellationToken);
        if (version.BranchLimit.HasValue && branchCount > version.BranchLimit) throw new InvalidOperationException("El cliente excede la cuota de sucursales del plan.");
        if (version.UserLimit.HasValue && userCount > version.UserLimit) throw new InvalidOperationException("El cliente excede la cuota de usuarios del plan.");
        var modules = version.Modules.Select(x => x.Module.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (branchCount > 1 && !modules.Contains(TenantModules.MultiBranch))
            throw new InvalidOperationException("El cliente depende del mÃ³dulo multi-sucursal.");
        if (!modules.Contains(TenantModules.Expenses) && await _context.Expenses.AnyAsync(x => x.TenantId == id, cancellationToken))
            throw new InvalidOperationException("El cliente tiene gastos registrados.");
        if (!modules.Contains(TenantModules.CashRegister) && await _context.CashRegisterClosures.AnyAsync(x => x.TenantId == id, cancellationToken))
            throw new InvalidOperationException("El cliente tiene cierres de caja registrados.");
        if (!modules.Contains(TenantModules.BusinessDocuments) && await _context.BusinessDocuments.AnyAsync(x => x.TenantId == id, cancellationToken))
            throw new InvalidOperationException("El cliente tiene documentos empresariales registrados.");
        if (!modules.Contains(TenantModules.DeliveryRouting) && await _context.DeliveryRoutes.AnyAsync(x => x.TenantId == id, cancellationToken))
            throw new InvalidOperationException("El cliente tiene rutas de domicilio registradas.");
        if (!modules.Contains(TenantModules.DeliveryTracking) && await _context.DeliveryTrackingIncidents.AnyAsync(x => EF.Property<int?>(x, "TenantId") == id, cancellationToken))
            throw new InvalidOperationException("El cliente tiene incidentes de seguimiento registrados.");
        if (!modules.Contains(TenantModules.CostAttribution) && await _context.ExpenseMenuTargets.AnyAsync(x => x.TenantId == id, cancellationToken))
            throw new InvalidOperationException("El cliente tiene imputaciones de costos al menÃº.");

        var current = await _context.TenantSubscriptions.SingleAsync(x => x.TenantId == id && x.Status == TenantSubscriptionStatus.Active, cancellationToken);
        var before = Snapshot(current);
        current.Status = TenantSubscriptionStatus.Ended; current.EndsAt = DateTime.UtcNow;
        var replacement = new TenantSubscription { TenantId = id, PlanVersionId = version.Id, Status = TenantSubscriptionStatus.Active, StartsAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        _context.TenantSubscriptions.Add(replacement);
        await AuditAsync("tenant.subscription", nameof(TenantSubscription), id.ToString(), before, replacement, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return await LoadTenantDetailAsync(id, cancellationToken);
    }

    public async Task<PlatformTenantDetailDto> SetAddonAsync(int id, string addonCode, bool active, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var addon = await _context.SaasAddons.SingleOrDefaultAsync(x => x.Code == addonCode && x.Active, cancellationToken) ?? throw new KeyNotFoundException("Add-on no encontrado.");
        var assignment = await _context.TenantAddons.SingleOrDefaultAsync(x => x.TenantId == id && x.AddonId == addon.Id, cancellationToken);
        var before = assignment is null ? null : Snapshot(assignment);
        if (assignment is null)
        {
            assignment = new TenantAddon { TenantId = id, AddonId = addon.Id };
            _context.TenantAddons.Add(assignment);
        }
        assignment.Active = active; assignment.EnabledAt = active ? DateTime.UtcNow : assignment.EnabledAt;
        assignment.DisabledAt = active ? null : DateTime.UtcNow; assignment.UpdatedAt = DateTime.UtcNow;
        await AuditAsync("tenant.addon", nameof(TenantAddon), $"{id}:{addonCode}", before, assignment, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return await LoadTenantDetailAsync(id, cancellationToken);
    }

    public async Task ResendInvitationAsync(int tenantId, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var tenant = await _context.Tenants.SingleAsync(x => x.Id == tenantId, cancellationToken);
        var user = await _context.Users.SingleAsync(x => x.TenantId == tenantId && x.Role == UserRole.Superadmin, cancellationToken);
        var branch = await _context.Branches.SingleAsync(x => x.Id == user.BranchId, cancellationToken);
        var active = await _context.TenantInvitations.Where(x => x.TenantId == tenantId && x.AcceptedAt == null && x.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var item in active) item.RevokedAt = DateTime.UtcNow;
        var invitation = await CreateInvitationAsync(tenant, branch, user, cancellationToken);
        await AuditAsync("tenant.invitation.resend", nameof(TenantInvitation), invitation.Invitation.PublicId.ToString(), null, invitation.Invitation, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await SendInvitationAsync(invitation.Invitation, invitation.Token, user.Name, tenant.DisplayName);
    }

    public async Task AcceptInvitationAsync(AcceptTenantInvitationRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        if (request.Password.Length < 8) throw new InvalidOperationException("La contraseña debe tener al menos 8 caracteres.");
        var invitation = await _context.TenantInvitations.Include(x => x.User).Include(x => x.Tenant)
            .SingleOrDefaultAsync(x => x.PublicId == request.InvitationId, cancellationToken) ?? throw new UnauthorizedAccessException("Invitación inválida.");
        if (invitation.AcceptedAt is not null || invitation.RevokedAt is not null || invitation.ExpiresAt <= DateTime.UtcNow
            || !FixedEquals(invitation.TokenHash, PlatformAuthService.Hash(request.Token)))
            throw new UnauthorizedAccessException("Invitación inválida o expirada.");
        invitation.AcceptedAt = DateTime.UtcNow;
        invitation.User.PasswordHash = _passwords.HashPassword(request.Password); invitation.User.Active = true;
        invitation.Tenant.Status = TenantStatus.Active; invitation.Tenant.StatusReason = null; invitation.Tenant.UpdatedAt = DateTime.UtcNow; invitation.Tenant.AccessVersion++;
        await AuditAsync("tenant.invitation.accept", nameof(TenantInvitation), invitation.PublicId.ToString(), null,
            new { invitation.TenantId, invitation.BranchId, invitation.UserId, invitation.Email, invitation.AcceptedAt },
            new PlatformRequestContext("invitation", "public", invitation.PublicId.ToString()), cancellationToken, invitation.Email);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var plans = await _context.SaasPlans.AsNoTracking().Include(x => x.Versions).ThenInclude(x => x.Modules).ThenInclude(x => x.Module).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return plans.Select(MapPlan).ToList();
    }

    public async Task<PlatformPlanDto> CreatePlanAsync(CreatePlatformPlanRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var code = request.Code.Trim().ToLowerInvariant();
        if (await _context.SaasPlans.AnyAsync(x => x.Code == code, cancellationToken)) throw new InvalidOperationException("El código del plan ya existe.");
        var plan = new SaasPlan { Code = code, Name = request.Name.Trim(), Description = request.Description.Trim(), Active = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _context.SaasPlans.Add(plan);
        await AuditAsync("plan.create", nameof(SaasPlan), code, null, plan, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return MapPlan(plan);
    }

    public async Task<PlatformPlanVersionDto> CreatePlanVersionAsync(int planId, UpsertPlanVersionRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var plan = await _context.SaasPlans.SingleOrDefaultAsync(x => x.Id == planId, cancellationToken) ?? throw new KeyNotFoundException("Plan no encontrado.");
        var next = (await _context.SaasPlanVersions.Where(x => x.PlanId == planId).MaxAsync(x => (int?)x.VersionNumber, cancellationToken) ?? 0) + 1;
        var version = new SaasPlanVersion { PlanId = plan.Id, VersionNumber = next, Status = PlanVersionStatus.Draft, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        ApplyVersion(version, request);
        await SetVersionModulesAsync(version, request.Modules, cancellationToken);
        _context.SaasPlanVersions.Add(version);
        await AuditAsync("plan.version.create", nameof(SaasPlanVersion), $"{planId}:{next}", null, version, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return MapVersion(version);
    }

    public async Task<PlatformPlanVersionDto> UpdatePlanVersionAsync(int versionId, UpsertPlanVersionRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var version = await _context.SaasPlanVersions.Include(x => x.Modules).ThenInclude(x => x.Module).SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken) ?? throw new KeyNotFoundException("Versión no encontrada.");
        EnsureDraft(version); var before = Snapshot(version); ApplyVersion(version, request); await SetVersionModulesAsync(version, request.Modules, cancellationToken); version.UpdatedAt = DateTime.UtcNow;
        await AuditAsync("plan.version.update", nameof(SaasPlanVersion), versionId.ToString(), before, version, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return MapVersion(version);
    }

    public async Task<PlatformPlanVersionDto> PublishPlanVersionAsync(int versionId, PlatformRequestContext requestContext, CancellationToken cancellationToken = default) =>
        await ChangeVersionStatusAsync(versionId, PlanVersionStatus.Published, "plan.version.publish", requestContext, cancellationToken);

    public async Task<PlatformPlanVersionDto> RetirePlanVersionAsync(int versionId, PlatformRequestContext requestContext, CancellationToken cancellationToken = default) =>
        await ChangeVersionStatusAsync(versionId, PlanVersionStatus.Retired, "plan.version.retire", requestContext, cancellationToken);

    public async Task<IReadOnlyList<PlatformCatalogItemDto>> GetModulesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        return await _context.SaasModules.AsNoTracking().OrderBy(x => x.DisplayOrder).Select(x => new PlatformCatalogItemDto(x.Id, x.Code, x.Name, x.Description, x.Category, x.Active, x.DisplayOrder)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformCatalogItemDto>> GetAddonsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        return await _context.SaasAddons.AsNoTracking().OrderBy(x => x.DisplayOrder).Select(x => new PlatformCatalogItemDto(x.Id, x.Code, x.Name, x.Description, "addon", x.Active, x.DisplayOrder)).ToListAsync(cancellationToken);
    }

    public async Task<PlatformCatalogItemDto> UpsertModuleAsync(int? id, UpsertPlatformCatalogItemRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var code = NormalizeCatalogCode(request.Code);
        var entity = id.HasValue
            ? await _context.SaasModules.SingleOrDefaultAsync(x => x.Id == id.Value, cancellationToken) ?? throw new KeyNotFoundException("Modulo no encontrado.")
            : new SaasModule { Code = code };
        if (id.HasValue && entity.Code != code) throw new InvalidOperationException("El codigo estable de un modulo no puede cambiarse.");
        if (!id.HasValue && await _context.SaasModules.AnyAsync(x => x.Code == code, cancellationToken)) throw new InvalidOperationException("El codigo del modulo ya existe.");
        var before = id.HasValue ? Snapshot(entity) : null;
        entity.Name = request.Name.Trim();
        entity.Description = request.Description.Trim();
        entity.Category = request.Category.Trim();
        entity.Active = request.Active;
        entity.DisplayOrder = request.DisplayOrder;
        if (!id.HasValue) _context.SaasModules.Add(entity);
        await AuditAsync(id.HasValue ? "module.update" : "module.create", nameof(SaasModule), code, before, entity, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return new PlatformCatalogItemDto(entity.Id, entity.Code, entity.Name, entity.Description, entity.Category, entity.Active, entity.DisplayOrder);
    }

    public async Task<PlatformCatalogItemDto> UpsertAddonAsync(int? id, UpsertPlatformCatalogItemRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var code = NormalizeCatalogCode(request.Code);
        var entity = id.HasValue
            ? await _context.SaasAddons.SingleOrDefaultAsync(x => x.Id == id.Value, cancellationToken) ?? throw new KeyNotFoundException("Add-on no encontrado.")
            : new SaasAddon { Code = code };
        if (id.HasValue && entity.Code != code) throw new InvalidOperationException("El codigo estable de un add-on no puede cambiarse.");
        if (!id.HasValue && await _context.SaasAddons.AnyAsync(x => x.Code == code, cancellationToken)) throw new InvalidOperationException("El codigo del add-on ya existe.");
        var before = id.HasValue ? Snapshot(entity) : null;
        entity.Name = request.Name.Trim();
        entity.Description = request.Description.Trim();
        entity.Active = request.Active;
        entity.DisplayOrder = request.DisplayOrder;
        if (!id.HasValue) _context.SaasAddons.Add(entity);
        await AuditAsync(id.HasValue ? "addon.update" : "addon.create", nameof(SaasAddon), code, before, entity, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return new PlatformCatalogItemDto(entity.Id, entity.Code, entity.Name, entity.Description, "addon", entity.Active, entity.DisplayOrder);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        return await _context.PlatformSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => DeserializeSetting(x.ValueJson), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> UpdateSettingsAsync(IReadOnlyDictionary<string, string> settings, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        foreach (var (key, value) in settings)
        {
            if (key.Contains("secret", StringComparison.OrdinalIgnoreCase) || key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("token", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Los secretos deben permanecer en variables de entorno.");
            var entity = await _context.PlatformSettings.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
            if (entity is null) { entity = new PlatformSetting { Key = key }; _context.PlatformSettings.Add(entity); }
            entity.ValueJson = JsonSerializer.Serialize(value); entity.UpdatedAt = DateTime.UtcNow;
        }
        await AuditAsync("settings.update", nameof(PlatformSetting), "global", null, settings, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetSettingsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformAuditDto>> GetAuditAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        return await _context.PlatformAuditLogs.AsNoTracking().OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PlatformAuditDto(x.Id, x.ActorIdentifier, x.Action, x.EntityType, x.EntityId, x.BeforeJson, x.AfterJson, x.IpAddress, x.CorrelationId, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<PlatformTenantDetailDto> LoadTenantDetailAsync(int id, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants.AsNoTracking().Include(x => x.Branches).Include(x => x.Users).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Cliente no encontrado.");
        var subscription = await _context.TenantSubscriptions.AsNoTracking().Include(x => x.PlanVersion).ThenInclude(x => x.Plan).Include(x => x.PlanVersion).ThenInclude(x => x.Modules).ThenInclude(x => x.Module)
            .SingleAsync(x => x.TenantId == id && x.Status == TenantSubscriptionStatus.Active, cancellationToken);
        var addons = await _context.TenantAddons.AsNoTracking().Include(x => x.Addon).Where(x => x.TenantId == id && x.Active).Select(x => x.Addon.Code).OrderBy(x => x).ToListAsync(cancellationToken);
        var month = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var usage = await _context.TenantUsageMonthly.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == id && x.Month == month, cancellationToken);
        return new PlatformTenantDetailDto(tenant.Id, tenant.PublicId, tenant.DisplayName, tenant.Slug, tenant.ContactName, tenant.ContactEmail, tenant.ContactPhone,
            tenant.LegalName, tenant.TaxId, tenant.BillingAddress, tenant.Status.ToString().ToLowerInvariant(), tenant.StatusReason, MapVersion(subscription.PlanVersion), addons,
            tenant.Branches.Select(x => new PlatformTenantBranchDto(x.Id, x.Name, x.Address, true)).ToList(),
            tenant.Users.Select(x => new PlatformTenantUserDto(x.Id, x.Name, x.Email, x.Role?.ToString() ?? string.Empty, x.Active)).ToList(),
            new TenantUsageDto(usage?.Orders ?? 0, usage?.StorageBytes ?? 0, usage?.AiInputTokens ?? 0, usage?.AiOutputTokens ?? 0, usage?.AiEstimatedCostUsd ?? 0), tenant.CreatedAt, tenant.UpdatedAt);
    }

    private async Task<(TenantInvitation Invitation, string Token)> CreateInvitationAsync(Tenant tenant, Branch branch, User user, CancellationToken cancellationToken)
    {
        var rawToken = GenerateToken();
        var invitation = new TenantInvitation { TenantId = tenant.Id, BranchId = branch.Id, UserId = user.Id, Email = user.Email, TokenHash = PlatformAuthService.Hash(rawToken), ExpiresAt = DateTime.UtcNow.AddHours(72), CreatedAt = DateTime.UtcNow };
        _context.TenantInvitations.Add(invitation);
        await _context.SaveChangesAsync(cancellationToken);
        return (invitation, rawToken);
    }

    private async Task SendInvitationAsync(TenantInvitation invitation, string token, string name, string tenantName)
    {
        var link = $"{_invitationUrl}?invitationId={invitation.PublicId:D}&token={Uri.EscapeDataString(token)}";
        var result = await _email.SendTenantInvitationEmailAsync(invitation.Email, name, tenantName, link, invitation.ExpiresAt);
        if (!result.Success) throw new InvalidOperationException("El cliente fue creado, pero no se pudo enviar la invitación.");
    }

    private async Task SetVersionModulesAsync(SaasPlanVersion version, IReadOnlyList<string> codes, CancellationToken cancellationToken)
    {
        var normalized = codes.Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
        var modules = await _context.SaasModules.Where(x => normalized.Contains(x.Code) && x.Active).ToListAsync(cancellationToken);
        if (modules.Count != normalized.Count) throw new InvalidOperationException("Uno o más módulos no existen o están inactivos.");
        version.Modules.Clear();
        foreach (var module in modules) version.Modules.Add(new SaasPlanVersionModule { Module = module });
    }

    private async Task<PlatformPlanVersionDto> ChangeVersionStatusAsync(int versionId, PlanVersionStatus status, string action, PlatformRequestContext requestContext, CancellationToken cancellationToken)
    {
        using var scope = _executionContext.BeginSystemScope();
        var version = await _context.SaasPlanVersions.Include(x => x.Modules).ThenInclude(x => x.Module).SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken) ?? throw new KeyNotFoundException("Versión no encontrada.");
        if (status == PlanVersionStatus.Published) EnsureDraft(version);
        if (status == PlanVersionStatus.Retired && version.Status != PlanVersionStatus.Published) throw new InvalidOperationException("Solo una versión publicada puede retirarse.");
        var before = Snapshot(version); version.Status = status; version.PublishedAt = status == PlanVersionStatus.Published ? DateTime.UtcNow : version.PublishedAt; version.UpdatedAt = DateTime.UtcNow;
        await AuditAsync(action, nameof(SaasPlanVersion), versionId.ToString(), before, version, requestContext, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return MapVersion(version);
    }

    private void ApplyVersion(SaasPlanVersion version, UpsertPlanVersionRequest request)
    {
        if (request.BranchLimit is <= 0 || request.UserLimit is <= 0) throw new InvalidOperationException("Los límites deben ser positivos o ilimitados.");
        version.Currency = request.Currency.Trim().ToUpperInvariant(); version.MonthlyPrice = request.MonthlyPrice; version.AnnualPrice = request.AnnualPrice;
        version.BranchLimit = request.BranchLimit; version.UserLimit = request.UserLimit;
    }

    private async Task AuditAsync(string action, string entityType, string entityId, object? before, object? after, PlatformRequestContext requestContext, CancellationToken cancellationToken, string? actorIdentifier = null)
    {
        var actor = actorIdentifier ?? (_currentUser.IsAuthenticated ? _currentUser.Email : null);
        if (string.IsNullOrWhiteSpace(actor)) return;
        _context.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            PlatformUserId = _currentUser.IsAuthenticated ? _currentUser.Id : null, ActorIdentifier = actor, Action = action, EntityType = entityType, EntityId = entityId,
            BeforeJson = before is string text ? text : Snapshot(before), AfterJson = Snapshot(after), IpAddress = requestContext.IpAddress,
            CorrelationId = requestContext.CorrelationId, CreatedAt = DateTime.UtcNow
        });
        await Task.CompletedTask;
    }

    private static PlatformPlanDto MapPlan(SaasPlan plan) => new(plan.Id, plan.Code, plan.Name, plan.Description, plan.Active, plan.Versions.OrderByDescending(x => x.VersionNumber).Select(MapVersion).ToList());
    private static PlatformPlanVersionDto MapVersion(SaasPlanVersion version) => new(version.Id, version.VersionNumber, version.Status.ToString().ToLowerInvariant(), version.Currency,
        version.MonthlyPrice, version.AnnualPrice, version.BranchLimit, version.UserLimit, version.Modules.Select(x => x.Module.Code).OrderBy(x => x).ToList(), version.PublishedAt, version.CreatedAt);
    private static void EnsureDraft(SaasPlanVersion version) { if (version.Status != PlanVersionStatus.Draft) throw new InvalidOperationException("Una versión publicada o retirada es inmutable."); }
    private static string Snapshot(object? value) => PlatformAuditSerializer.Serialize(value);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    private static string DeserializeSetting(string value) { try { return JsonSerializer.Deserialize<string>(value) ?? string.Empty; } catch { return value; } }
    private static void ValidateSlug(string slug) { if (!SlugPattern().IsMatch(slug.Trim())) throw new InvalidOperationException("El slug solo admite letras minúsculas, números y guiones."); }
    private static string NormalizeCatalogCode(string code)
    {
        var normalized = code.Trim().ToLowerInvariant();
        if (!CatalogCodePattern().IsMatch(normalized)) throw new InvalidOperationException("El codigo solo admite letras minusculas, numeros y guion bajo.");
        return normalized;
    }
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
    [GeneratedRegex("^[a-z0-9]+(?:_[a-z0-9]+)*$")]
    private static partial Regex CatalogCodePattern();
}
