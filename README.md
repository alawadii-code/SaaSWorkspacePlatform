# workspace

a multi-tenant SaaS workspace platform: people belong to one or more orgs, each
w/ its own projects, tasks, members n audit trail.

ASP.NET Core 10 (minimal APIs) · PostgreSQL 16 + EF Core · redis-backed job
queue · React 19 + TypeScript + Vite · Docker Compose.

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — tenancy, authorization,
  auditing, background work n the tradeoffs behind all of it
- [docs/API.md](docs/API.md) — endpoints, required permissions, error contract.
  the running API also serves `/docs` n `/openapi/v1.json`

## quick start

needs Docker w/ Compose v2.

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
| web app | <http://localhost:8080> |
| API | <http://localhost:5080> |
| API reference | <http://localhost:5080/docs> |
| mail catcher | <http://localhost:8025> |
| health | `/health` n `/health/ready` |

the API applies migrations on startup sOoo the stack is usable as soon as the
containers go healthy.

### walkthrough

1. make an account at <http://localhost:8080> (password: 12+ chars)
2. create an org — u become its owner
3. **People → Invite someone**, as a Member
4. open <http://localhost:8025>. the invitation email is sitting there, sent by
   the worker n not by the web request. follow the accept link
5. register w/ the invited address n accept
6. as that member, notice theres no "create project" control, n the API returns
   403 if u call it directly anyway. make a task instead
7. back as the owner, open the audit log: evry action is there w/ its actor

```bash
docker compose logs -f api worker
docker compose exec postgres psql -U workspace -d workspace
docker compose down -v
```

## running w/o docker

needs .NET SDK 10, Node 22, PostgreSQL 16 n Redis 7.

```bash
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=workspace;Username=postgres;Password=postgres"
export Jwt__SigningKey="$(openssl rand -base64 48)"

dotnet run --project src/Workspace.Api        # http://localhost:5080
dotnet run --project src/Workspace.Worker     # second shell, same two variables
cd web && npm install && npm run dev          # http://localhost:5173
```

the dev server proxies `/api` to the API sOoo the browser sees one origin n the
refresh cookie stays first-party. w/o redis just set `Queue__Provider=memory`.
email is logged insteada sent unless `Smtp__Enabled=true`.

## tests

```bash
dotnet test tests/Workspace.UnitTests

export WORKSPACE_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres"
dotnet test tests/Workspace.IntegrationTests
```

integration tests create a uniquely named database per run n drop it after, sOoo
they nvr collide n leave nuthin behind. if the stack is already up just point
`WORKSPACE_TEST_POSTGRES` at the compose database on `localhost:5432`.

what they cover:

| area | covered |
| --- | --- |
| tenant isolation | another tenants owner gets 404 for an org, project, task list, audit trail n invitations; a task cant be created in another tenants project or assigned to a non-member |
| authorization | each role against each operation; a member can only edit their own tasks; an admin cant grant Owner or act on an owner; nobody changes their own role; the last owner cant leave |
| authentication | registration, sign-in, refresh rotation, replay detection killing the session, sign-out, lockout, identical responses for wrong password vs unknown account |
| invitations | queued not sent inline, delivered by the worker, single-use, email-bound, revocable, resendable, expiring, failures recorded |
| audit | written in the actions transaction, tenant-scoped, rejected on update n delete by both app n database, survives tenant deletion |
| operations | liveness, readiness, OpenAPI document, security headers, malformed JSON handled w/o a stack trace |

frontend: `npm run build` in `web/` typechecks n builds.

## layout

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

## configuration

read from the environment; `__` maps to a config separator, sOoo
`Jwt__SigningKey` sets `Jwt:SigningKey`.

| variable | default | notes |
| --- | --- | --- |
| `ConnectionStrings__Postgres` | — | required |
| `Jwt__SigningKey` | — | required, 32+ chars. startup fails outside Development if itz missing or still the example value |
| `Jwt__AccessTokenMinutes` | `15` | |
| `Jwt__RefreshTokenDays` | `14` | |
| `Auth__UseSecureCookies` | `true` | startup fails outside Development if false |
| `Auth__ReturnRefreshTokenInBody` | `false` | for non-browser clients |
| `Database__MigrateOnStartup` | `true` | set false in prod n migrate as a release step |
| `Queue__Provider` | `redis` | `memory` runs w/o redis |
| `Smtp__Enabled` | `false` | when false, email is logged |
| `Invitations__LifetimeDays` | `7` | |
| `Invitations__AcceptUrlTemplate` | localhost | `{token}` gets substituted |
| `Cors__AllowedOrigins__0` | — | only needed when the frontend is on another origin |

`.env` is git-ignored n `.env.example` holds placeholders only. no credential
is committed anywhere in this repo.

## database

schema lives in
[`Persistence/Migrations/Sql/`](src/Workspace.Infrastructure/Persistence/Migrations/Sql),
applied by EF Core n tracked in `__EFMigrationsHistory`. that folders README
explains why itz written as SQL.

```
users ──< memberships >── organizations ──< projects ──< tasks
                          organizations ──< invitations
                          users ──< refresh_tokens
                          audit_logs (no foreign keys)
```
