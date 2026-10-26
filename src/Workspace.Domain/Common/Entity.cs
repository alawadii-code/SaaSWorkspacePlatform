namespace Workspace.Domain.Common;

/// <summary>Base type for every persisted entity.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Marks an entity as belonging to exactly one organization (tenant).</summary>
public interface ITenantOwned
{
    Guid OrganizationId { get; }
}
