# Workspace — multi-tenant SaaS platform

A workspace platform where people belong to one or more organizations, each
with its own projects, tasks, members and audit trail. Built for the developer
trial brief: authentication, role-based permissions, project and task
management, audit logging and background invitation jobs.

**Stack:** ASP.NET Core 10 (minimal APIs) · PostgreSQL 16 + EF Core ·
Redis-backed job queue · React 19 + TypeScript + Vite · Docker Compose.

- [Architecture and trade-offs](docs/ARCHITECTURE.md) — how tenancy, authorization,
  auditing and background work are enforced, and what was deliberately left out.
- [API reference](docs/API.md) — every endpoint, its required permission, and the
  error contract. The running API also serves [`/docs`](http://localhost:5080/docs)
  and an OpenAPI document at `/openapi/v1.json`.

---

## Quick start

Requirements: Docker with Compose v2. Nothing else — no SDK, no Node, no local
database.

```bash
git clone <this-repo> && cd saas-workspace

cp .env.example .env

# Generate the two secrets the stack needs, into .env
sed -i.bak "s|^JWT_SIGNING_KEY=.*|JWT_SIGNING_KEY=$(openssl rand -base64 48)|" .env
sed -i.bak "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=')|" .env
rm -f .env.bak

docker compose up --build
```

Then:

| What | Where |
| --- | --- |
| Web app | <http://localhost:8080> |
| API | <http://localhost:5080> |
| API reference (Scalar) | <http://localhost:5080/docs> |
| OpenAPI document | <http://localhost:5080/openapi/v1.json> |
| Mail catcher (Mailpit) | <http://localhost:8025> |
| Health / readiness | <http://localhost:5080/health> · `/health/ready` |

The API applies database migrations on startup, so the stack is usable as soon
as the containers report healthy.

### Try the whole flow in two minutes

1. Open <http://localhost:8080> and **create an account** (password: 12+
   characters).
2. **Create an organization** — you become its owner.
3. Open **People → Invite someone** and invite a second address, for example
   `teammate@example.test`, as a **Member**.
4. Open **Mailpit** at <http://localhost:8025>: the invitation email is there,
   sent by the background worker rather than by the web request. Open it and
   follow the accept link.
5. Register that second account with the invited address and accept. It lands
   in the organization as a Member.
6. As the member, try to create a project — the control is not offered, and the
   API answers `403` if you call it directly. Create a **task** instead; that
   is what a member may do.
7. Back as the owner, open the **Audit log**: every one of those actions is
   recorded with who did it, when, and from where.

### Useful commands

```bash
docker compose logs -f api worker     # follow the API and worker
docker compose exec postgres psql -U workspace -d workspace
docker compose down -v                # stop and delete the database volume
```

---

## Running without Docker

Requirements: .NET SDK 10, Node 22, and PostgreSQL 16 and Redis 7 reachable
locally.

```bash
# 1. API (applies migrations on startup)
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=workspace;Username=postgres;Password=postgres"
export Jwt__SigningKey="$(openssl rand -base64 48)"
dotnet run --project src/Workspace.Api        # http://localhost:5080

# 2. Background worker, in a second shell (same two variables)
dotnet run --project src/Workspace.Worker

# 3. Frontend, in a third shell
cd web && npm install && npm run dev          # http://localhost:5173
```

The Vite dev server proxies `/api` to `http://localhost:5080`, so the browser
sees a single origin and the refresh cookie stays first-party — the same
arrangement nginx provides in Compose.

Without Redis, set `Queue__Provider=memory` to run the API and worker in a
single process group with an in-memory queue.

Email in development: the API defaults to `Smtp__Enabled=false`, which writes
messages to the log instead of sending them. Point `Smtp__Host`/`Smtp__Port` at
Mailpit (`localhost:1025`) and set `Smtp__Enabled=true` to see real delivery.

---

## Tests

```bash
# Unit tests — pure logic, no database
dotnet test tests/Workspace.UnitTests

# Integration tests — the real API against a real PostgreSQL
export WORKSPACE_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres"
dotnet test tests/Workspace.IntegrationTests

# Everything
dotnet test
```

Integration tests create a uniquely named database per run and drop it
afterwards, so they never collide and leave nothing behind. If the stack is
already running, point `WORKSPACE_TEST_POSTGRES` at the Compose database (it is
published on `localhost:5432`).

What the suites cover, and why those things:

| Area | Examples |
| --- | --- |
| Tenant isolation | A second tenant's owner gets `404` for another tenant's organization, project, task list, audit trail and invitations; a task cannot be created in another tenant's project or assigned to a non-member |
| Authorization | Every role against every operation; a member may edit only tasks they created or are assigned to; an admin can neither grant Owner nor touch an owner; nobody changes their own role; the last owner cannot leave |
| Authentication | Registration, sign-in, refresh-token rotation, replay detection revoking the session, sign-out, account lockout, and identical responses for wrong password and unknown account |
| Invitations | Queued rather than sent inline, delivered by the worker with a working accept link, single-use, email-bound, revocable, resendable, expiring, and recorded when delivery fails |
| Audit | Written with the action in one transaction, scoped per tenant, refused on update or delete by both the application and the database, and surviving deletion of the organization |
| Operations | Liveness and readiness, OpenAPI document, security headers, malformed JSON handled as a problem document with no stack trace |

Frontend checks run with `npm run build` in `web/`, which typechecks and builds.

---

## Project layout

```
src/
  Workspace.Domain/          entities, enums, RBAC matrix, membership invariants (no dependencies)
  Workspace.Application/     use cases, DTOs, validators, abstractions, job handlers
  Workspace.Infrastructure/  EF Core, migrations, security, Redis queue, SMTP
  Workspace.Api/             HTTP endpoints, auth pipeline, problem details, OpenAPI
  Workspace.Worker/          job processor and periodic maintenance
tests/
  Workspace.UnitTests/       pure logic
  Workspace.IntegrationTests/ the real API against a real database
web/                         React + TypeScript frontend
docker/                      Dockerfiles and nginx config
docs/                        architecture and API reference
```

Dependencies point inward: `Api` and `Worker` → `Infrastructure` →
`Application` → `Domain`. The domain project has no package references at all,
which is what keeps the permission matrix testable without a database, an HTTP
context or a DI container.

---

## Configuration

Every setting is read from the environment (or `appsettings.json` for
defaults). ASP.NET Core maps `__` to a configuration section separator, so
`Jwt__SigningKey` sets `Jwt:SigningKey`.

| Variable | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings__Postgres` | — | Required. |
| `Jwt__SigningKey` | — | Required, 32+ characters. Startup fails outside Development if missing or left at the example value. |
| `Jwt__AccessTokenMinutes` | `15` | Access tokens are short because identity, unlike role, is not re-read per request. |
| `Jwt__RefreshTokenDays` | `14` | |
| `Auth__UseSecureCookies` | `true` | Startup fails outside Development if false. |
| `Auth__ReturnRefreshTokenInBody` | `false` | For non-browser clients; off by default because a token in the body is readable by scripts. |
| `Database__MigrateOnStartup` | `true` | Set false in production and migrate as a release step. |
| `Queue__Provider` | `redis` | `memory` runs without Redis. |
| `Queue__ConnectionString` | `localhost:6379` | |
| `Smtp__Enabled` | `false` | When false, email is logged instead of sent. |
| `Smtp__Host` / `Smtp__Port` | `localhost` / `1025` | Mailpit in Compose. |
| `Invitations__LifetimeDays` | `7` | |
| `Invitations__AcceptUrlTemplate` | localhost | `{token}` is substituted. |
| `Cors__AllowedOrigins__0` | — | Only needed when the frontend is on another origin. |
| `RateLimiting__*` | enabled | Per-address limits on auth and invitation lookup. |

### Secrets

`.env` is git-ignored; `.env.example` contains placeholders only. No password,
key or token is committed anywhere in this repository, and the API refuses to
start outside Development with the example signing key or with insecure
cookies.

---

## Database

The schema lives in
[`src/Workspace.Infrastructure/Persistence/Migrations/Sql/`](src/Workspace.Infrastructure/Persistence/Migrations/Sql)
as plain SQL, applied by EF Core and tracked in `__EFMigrationsHistory`. See
that folder's [README](src/Workspace.Infrastructure/Persistence/Migrations/README.md)
for why, and how to add the next one.

```
users ──< memberships >── organizations ──< projects ──< tasks
                   organizations ──< invitations
                   users ──< refresh_tokens
                   audit_logs (no foreign keys, on purpose)
```

---

## What is deliberately not here

Honest scope notes, with reasoning in the [architecture
document](docs/ARCHITECTURE.md): no password reset or email verification flow,
no SSO, no file attachments, no realtime updates, no soft delete, and no
per-tenant encryption. Each is a feature rather than a missing guarantee — the
security properties the brief asks for are implemented and tested.
