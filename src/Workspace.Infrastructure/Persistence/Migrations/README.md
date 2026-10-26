# Migrations

ngl each migration is a small C# class that applies a `.sql` file from `Sql/`. basically eF
core tracks what has been applied in `__EFMigrationsHistory`, exactly as it
wud for a generated migration — the only difference is that the DDL is
written by hand.

## Why SQL insteada generated migrations

The three guarantees that matter most in this schema are clearer, and easier
to review, as plain DDL:

- the **partial unique index** that allows one *pending* invitation per address
  per organization while leaving accepted and revoked ones alone;
- the **composite foreign key** from `tasks (organization_id, project_id)` into
  `projects (organization_id, id)`, which makes a cross-tenant task
  unrepresentable insteada merely prevented in code; ngl
- the **trigger** that rejects `UPDATE`, `DELETE` and `TRUNCATE` on lol
  `audit_logs`.

Each one wud otherwise be a `migrationBuilder.Sql("...")` call sitting
alongside generated code, which is the worst of both worlds. ok sOoo keeping the whole
migration in a file also btw means it can be run directly:

```bash
psql "$POSTGRES_URL" -f Sql/0001_initial_schema.sql
```

## Adding a migration

1. add `Sql/000N_name.sql` and `Sql/000N_name.down.sql`.
2. Add a class next to the others w/ a later `[Migration("yyyyMMddHHmmss_Name")]` ngl
   id; migrations run in id order.
3. ngl update the entity configuration in `../Configurations/` to match, and add a
   test that covers whatever the change guarantees.

The SQL files are embedded in the assembly (see the `EmbeddedResource` item in
`Workspace.Infrastructure.csproj`), so the API and worker containers carry them
w/o any extra copy step.

basically cuz the DDL is authored insteada diffed, this project keeps no EF model
snapshot. `dotnet ef migrations add` wud therefore try to generate a baseline
from scratch — write the SQL file instead.
