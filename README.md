# Workspace

A multi-tenant SaaS workspace platform: people belong to one or more
organizations, each with its own projects, tasks, members and audit trail.

ASP.NET Core 10 (minimal APIs) · PostgreSQL 16 + EF Core · Redis-backed job
queue · React 19 + TypeScript + Vite · Docker Compose.

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — tenancy, authorization,
  auditing, background work, and the trade-offs behind them.
- [docs/API.md](docs/API.md) — endpoints, required permissions, error contract.
  The running API also serves `/docs` and `/openapi/v1.json`.

## Quick start

Requires Docker with Compose v2.

```bash
cp .env.example .env

# generate the two secrets the stack needs
sed -i.bak "s|^JWT_SIGNING_KEY=.*|JWT_SIGNING_KEY=$(openssl rand -base64 48)|" .env
sed -i.bak "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=')|" .env
rm -f .env.bak

docker compose up --build
```

| | |
| --- | --- |
| Web app | <http://localhost:8080> |
| API | <http://localhost:5080> |
| API reference | <http://localhost:5080/docs> |
| Mail catcher | <http://localhost:8025> |
| Health | `/health` and `/health/ready` |

The API applies migrations on startup, so the stack is usable once the
containers report healthy.

### Walkthrough

1. Create an account at <http://localhost:8080> (password: 12+ characters).
2. Create an organization. You become its owner.
3. **People → Invite someone**, as a Member.
4. Open <http://localhost:8025>. The invitation email is there, sent by the
   worker rather than by the web request. Follow the accept link.
5. Register with the invited address and accept.
6. As that member, note there is no "create project" control, and the API
   returns 403 if you call it directly. Create a task instead.
7. As the owner, open the audit log: every action is there with its actor.

```bash
docker compose logs -f api worker
docker compose exec postgres psql -U workspace -d workspace
docker compose down -v
```

## Running without Docker

Requires .NET SDK 10, Node 22, PostgreSQL 16 and Redis 7.

```bash
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=workspace;Username=postgres;Password=postgres"
export Jwt__SigningKey="$(openssl rand -base64 48)"

dotnet run --project src/Workspace.Api        # http://localhost:5080
dotnet run --project src/Workspace.Worker     # second shell, same two variables
cd web && npm install && npm run dev          # http://localhost:5173
```

The dev server proxies `/api` to the API, so the browser sees one origin and
the refresh cookie stays first-party. Without Redis, set
`Queue__Provider=memory`. Email is logged rather than sent unless
`Smtp__Enabled=true`.

## Tests

```bash
dotnet test tests/Workspace.UnitTests

export WORKSPACE_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres"
dotnet test tests/Workspace.IntegrationTests
```

Integration tests create a uniquely named database per run and drop it
afterwards. If the stack is already running, point `WORKSPACE_TEST_POSTGRES` at
the Compose database on `localhost:5432`.

Coverage:

| Area | Covered |
| --- | --- |
| Tenant isolation | Another tenant's owner gets 404 for an organization, project, task list, audit trail and invitations; a task cannot be created in another tenant's project or assigned to a non-member |
| Authorization | Each role against each operation; a member may edit only their own tasks; an admin cannot grant Owner or act on an owner; nobody changes their own role; the last owner cannot leave |
| Authentication | Registration, sign-in, refresh rotation, replay detection revoking the session, sign-out, lockout, identical responses for wrong password and unknown account |
| Invitations | Queued not sent inline, delivered by the worker, single-use, email-bound, revocable, resendable, expiring, failure recorded |
| Audit | Written in the action's transaction, tenant-scoped, rejected on update and delete by both application and database, surviving tenant deletion |
| Operations | Liveness, readiness, OpenAPI document, security headers, malformed JSON handled without a stack trace |

Frontend: `npm run build` in `web/` typechecks and builds.

## Layout

```
src/
  Workspace.Domain/           entities, enums, RBAC matrix, membership rules (no dependencies)
  Workspace.Application/      use cases, DTOs, validators, abstractions, job handlers
  Workspace.Infrastructure/   EF Core, migrations, security, Redis queue, SMTP
  Workspace.Api/              endpoints, auth pipeline, problem details, OpenAPI
  Workspace.Worker/           job processor and periodic maintenance
tests/
web/
docker/
docs/
```

## Configuration

Read from the environment; `__` maps to a configuration separator, so
`Jwt__SigningKey` sets `Jwt:SigningKey`.

| Variable | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings__Postgres` | — | Required |
| `Jwt__SigningKey` | — | Required, 32+ chars. Startup fails outside Development if missing or left at the example value |
| `Jwt__AccessTokenMinutes` | `15` | |
| `Jwt__RefreshTokenDays` | `14` | |
| `Auth__UseSecureCookies` | `true` | Startup fails outside Development if false |
| `Auth__ReturnRefreshTokenInBody` | `false` | For non-browser clients |
| `Database__MigrateOnStartup` | `true` | Set false in production; migrate as a release step |
| `Queue__Provider` | `redis` | `memory` runs without Redis |
| `Smtp__Enabled` | `false` | When false, email is logged |
| `Invitations__LifetimeDays` | `7` | |
| `Invitations__AcceptUrlTemplate` | localhost | `{token}` is substituted |
| `Cors__AllowedOrigins__0` | — | Only needed when the frontend is on another origin |

`.env` is git-ignored and `.env.example` holds placeholders only. No
credential is committed anywhere in this repository.

## Database

Schema in
[`Persistence/Migrations/Sql/`](src/Workspace.Infrastructure/Persistence/Migrations/Sql),
applied by EF Core and tracked in `__EFMigrationsHistory`. See that folder's
README for why it is written as SQL.

```
users ──< memberships >── organizations ──< projects ──< tasks
                          organizations ──< invitations
                          users ──< refresh_tokens
                          audit_logs (no foreign keys)
```
