using Microsoft.EntityFrameworkCore;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Domain.Audit;
using Workspace.Domain.Authorization;
using Workspace.Domain.Common;
using Workspace.Domain.Entities;

namespace Workspace.Application.Organizations;

/// <summary>
/// Tenant lifecycle: create an organization, read and rename the current one,
/// and delete it.
/// </summary>
public sealed class OrganizationService(
    IWorkspaceDb db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IAuditWriter audit,
    IClock clock)
{
    /// <summary>
    /// Creates an organization and makes the caller its first owner. Any
    /// authenticated user may do this; there is no tenant context yet.
    /// </summary>
    public async Task<OrganizationDto> CreateAsync(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var organization = new Organization
        {
            Name = request.Name.Trim(),
            Slug = await GenerateUniqueSlugAsync(request.Name, cancellationToken),
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Organizations.Add(organization);

        db.Memberships.Add(new Membership
        {
            OrganizationId = organization.Id,
            UserId = userId,
            Role = OrganizationRole.Owner,
            JoinedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        audit.RecordFor(
            organization.Id,
            userId,
            currentUser.Email,
            AuditActions.OrganizationCreated,
            AuditResources.Organization,
            organization.Id.ToString(),
            new { name = organization.Name, slug = organization.Slug });

        await db.SaveChangesAsync(cancellationToken);

        return new OrganizationDto(
            organization.Id,
            organization.Name,
            organization.Slug,
            OrganizationRole.Owner,
            RolePermissions.NamesFor(OrganizationRole.Owner),
            MemberCount: 1,
            ProjectCount: 0,
            organization.CreatedAt);
    }

    /// <summary>Details of the organization resolved for this request.</summary>
    public async Task<OrganizationDto> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.OrganizationRead);

        var organizationId = tenant.OrganizationId;

        var organization = await db.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization");

        // Counts come from tenant-filtered sets, so they cannot include other
        // tenants' rows even if the filter were the only guard.
        var memberCount = await db.Memberships.CountAsync(cancellationToken);
        var projectCount = await db.Projects.CountAsync(cancellationToken);

        return new OrganizationDto(
            organization.Id,
            organization.Name,
            organization.Slug,
            tenant.Role,
            RolePermissions.NamesFor(tenant.Role),
            memberCount,
            projectCount,
            organization.CreatedAt);
    }

    public async Task<OrganizationDto> UpdateAsync(
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.OrganizationUpdate);

        var organizationId = tenant.OrganizationId;

        var organization = await db.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization");

        var previousName = organization.Name;
        organization.Name = request.Name.Trim();
        organization.UpdatedAt = clock.UtcNow;

        audit.Record(
            AuditActions.OrganizationUpdated,
            AuditResources.Organization,
            organization.Id.ToString(),
            new { from = previousName, to = organization.Name });

        await db.SaveChangesAsync(cancellationToken);

        var memberCount = await db.Memberships.CountAsync(cancellationToken);
        var projectCount = await db.Projects.CountAsync(cancellationToken);

        return new OrganizationDto(
            organization.Id,
            organization.Name,
            organization.Slug,
            tenant.Role,
            RolePermissions.NamesFor(tenant.Role),
            memberCount,
            projectCount,
            organization.CreatedAt);
    }

    /// <summary>
    /// Deletes the organization and everything inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owners only, and the caller must echo the organization's slug in
    /// <paramref name="confirmSlug"/> — an irreversible, whole-tenant
    /// operation should not be reachable by a mistyped URL.
    /// </para>
    /// <para>
    /// Trade-off: this is a hard delete, cascading to memberships, invitations,
    /// projects and tasks. A production system would soft-delete with a
    /// retention window so an accidental deletion can be undone. Audit rows
    /// deliberately survive: they hold the organization id as plain data with
    /// no foreign key, so the record that the tenant was deleted outlives it.
    /// </para>
    /// </remarks>
    public async Task DeleteAsync(string? confirmSlug, CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.OrganizationDelete);

        var organizationId = tenant.OrganizationId;

        var organization = await db.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization");

        if (!string.Equals(confirmSlug?.Trim(), organization.Slug, StringComparison.Ordinal))
        {
            throw new ValidationFailedException(
                "confirm",
                $"To delete this organization, pass confirm={organization.Slug}.");
        }

        audit.Record(
            AuditActions.OrganizationDeleted,
            AuditResources.Organization,
            organization.Id.ToString(),
            new { name = organization.Name, slug = organization.Slug });

        db.Organizations.Remove(organization);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Produces a slug that is free at the time of writing; the unique index on
    /// <c>organizations.slug</c> is what actually guarantees uniqueness, and a
    /// losing race surfaces as a 409 from the database error mapper.
    /// </summary>
    private async Task<string> GenerateUniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var candidate = Slug.From(name);

        if (!await db.Organizations.AnyAsync(o => o.Slug == candidate, cancellationToken))
        {
            return candidate;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var suffixed = Slug.WithSuffix(candidate, Guid.NewGuid().ToString("N")[..6]);

            if (!await db.Organizations.AnyAsync(o => o.Slug == suffixed, cancellationToken))
            {
                return suffixed;
            }
        }

        throw new ConflictException("Could not allocate a unique organization handle. Try a different name.");
    }
}
