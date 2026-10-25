using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Workspace.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the trigger that makes <c>audit_logs</c> append-only in the database,
/// so the guarantee does not depend on application code being correct.
/// </summary>
[DbContext(typeof(WorkspaceDbContext))]
[Migration("20261026120100_AuditImmutability")]
public sealed class AuditImmutability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(MigrationSql.Read("0002_audit_immutability.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(MigrationSql.Read("0002_audit_immutability.down.sql"));
    }
}
