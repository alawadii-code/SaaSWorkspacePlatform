# Migrations

Each migration is a small C# class that applies a `.sql` file from `Sql/`. EF
Core tracks what has been applied in `__EFMigrationsHistory`, exactly as it
would for a generated migration — the only difference is that the DDL is
written by hand.

## Why SQL rather than generated migrations

The three guarantees that matter most in this schema are clearer, and easier
to review, as plain DDL:

- the **partial unique index** that allows one *pending* invitation per address
  per organization while leaving accepted and revoked ones alone;
- the **composite foreign key** from `tasks (organization_id, project_id)` into
  `projects (organization_id, id)`, which makes a cross-tenant task
  unrepresentable rather than merely prevented in code;
- the **trigger** that rejects `UPDATE`, `DELETE` and `TRUNCATE` on
  `audit_logs`.

Each one would otherwise be a `migrationBuilder.Sql("...")` call sitting
alongside generated code, which is the worst of both worlds. Keeping the whole
migration in a file also means it can be run directly:

```bash
psql "$POSTGRES_URL" -f Sql/0001_initial_schema.sql
```

## Adding a migration

1. Add `Sql/000N_name.sql` and `Sql/000N_name.down.sql`.
2. Add a class next to the others with a later `[Migration("yyyyMMddHHmmss_Name")]`
   id; migrations run in id order.
3. Update the entity configuration in `../Configurations/` to match, and add a
   test that covers whatever the change guarantees.

The SQL files are embedded in the assembly (see the `EmbeddedResource` item in
`Workspace.Infrastructure.csproj`), so the API and worker containers carry them
without any extra copy step.

Because the DDL is authored rather than diffed, this project keeps no EF model
snapshot. `dotnet ef migrations add` would therefore try to generate a baseline
from scratch — write the SQL file instead.
