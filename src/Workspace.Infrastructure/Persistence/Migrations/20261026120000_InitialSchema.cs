using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Workspace.Infrastructure.Persistence.Migrations;

/// <summary>
/// Creates every table, index and constraint the application starts from.
/// </summary>
[DbContext(typeof(WorkspaceDbContext))]
[Migration("20261026120000_InitialSchema")]
public sealed class InitialSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(MigrationSql.Read("0001_initial_schema.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(MigrationSql.Read("0001_initial_schema.down.sql"));
    }
}
