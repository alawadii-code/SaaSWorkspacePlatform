# API reference

Base URL in the Compose stack: `http://localhost:5080` (or `/api` through the
web origin at `http://localhost:8080`).

A generated OpenAPI document is served at `/openapi/v1.json`, and a browsable
reference at [`/docs`](http://localhost:5080/docs). This file is the prose
version: what each endpoint is for, and what it refuses to do.

---

## Conventions

### Authentication

Send the access token as a bearer token:

```http
Authorization: Bearer <access token>
```

Access tokens last 15 minutes and carry identity only — no role or permission
claim. Roles are read from the database on every request, so a role change or a
removal applies to the caller's next call.

The refresh token is returned as an `HttpOnly`, `SameSite=Lax` cookie scoped to
`/api/auth`. Browsers need no code for it. Non-browser clients can enable
`Auth:ReturnRefreshTokenInBody` and post it explicitly to `/api/auth/refresh`.

### Tenant scope

Everything tenant-specific lives under `/api/organizations/{organizationId}`.
The caller's membership of that organization is resolved per request; if there
is none, the response is **`404`, not `403`** — the API does not confirm that
another tenant's resources exist.

### Errors

Every failure is an RFC 9457 problem document:

```json
{
  "type": "https://docs.workspace.local/errors/forbidden",
  "title": "Forbidden",
  "status": 403,
  "detail": "Your role (Member) does not allow this action.",
  "instance": "POST /api/organizations/0199.../projects",
  "code": "forbidden",
  "correlationId": "0199c0ffee7a4b1e9f2d"
}
```

Branch on `code`, not on `detail`.

| `code` | Status | Meaning |
| --- | --- | --- |
| `unauthorized` | 401 | Missing, invalid or expired credential |
| `forbidden` | 403 | Authenticated, but the role does not allow it |
| `not_found` | 404 | No such resource — or it belongs to another tenant |
| `conflict` | 409 | Violates current state (duplicate, or an invariant) |
| `resource_exists` | 409 | Unique constraint hit at the database |
| `reference_invalid` | 409 | Referenced resource does not exist |
| `validation_failed` | 422 | Field-level problems, in `errors` |
| `rate_limited` | 429 | Too many requests; see `Retry-After` |
| `malformed_request` | 400 | Body could not be parsed |
| `internal_error` | 500 | Quote `correlationId` when reporting |

Validation failures add an `errors` object keyed by field:

```json
{
  "code": "validation_failed",
  "status": 422,
  "errors": { "password": ["Password must be at least 12 characters."] }
}
```

### Correlation

Every response carries `X-Correlation-Id`. Send your own (letters, digits,
`-`, `_`, up to 64 characters) and it is echoed back, written to the server log
and stored on any audit entry the request produces.

### Paging

List endpoints take `page` (default 1) and `pageSize` (default 20, **max
100** — a larger value is capped, not rejected) and return:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 42,
  "totalPages": 3,
  "hasNextPage": true
}
```

### Enumerations

Sent and returned as names, never numbers.

| Type | Values |
| --- | --- |
| Role | `Viewer`, `Member`, `Admin`, `Owner` |
| Project status | `Active`, `Archived` |
| Task status | `Todo`, `InProgress`, `Blocked`, `Done` |
| Task priority | `Low`, `Medium`, `High`, `Urgent` |
| Invitation status | `Pending`, `Accepted`, `Revoked`, `Expired` |
| Delivery status | `Pending`, `Sent`, `Failed` |

### Rate limits

| Scope | Default |
| --- | --- |
| `/api/auth/*` | 10 per minute per address |
| `/api/invitations/preview` | 20 per minute per address |
| Everything else | 600 per minute per caller |

---

## Permissions

What each role may do. Endpoints below name the permission they require.

| Permission | Viewer | Member | Admin | Owner |
| --- | :-: | :-: | :-: | :-: |
| `OrganizationRead` | ● | ● | ● | ● |
| `MemberRead` | ● | ● | ● | ● |
| `ProjectRead` | ● | ● | ● | ● |
| `TaskRead` | ● | ● | ● | ● |
| `TaskCreate` | | ● | ● | ● |
| `TaskUpdateOwn` | | ● | ● | ● |
| `TaskAssign` | | ● | ● | ● |
| `OrganizationUpdate` | | | ● | ● |
| `MemberInvite` | | | ● | ● |
| `MemberRoleUpdate` | | | ● | ● |
| `MemberRemove` | | | ● | ● |
| `InvitationRead` | | | ● | ● |
| `InvitationRevoke` | | | ● | ● |
| `ProjectCreate` | | | ● | ● |
| `ProjectUpdate` | | | ● | ● |
| `ProjectDelete` | | | ● | ● |
| `TaskUpdateAny` | | | ● | ● |
| `TaskDelete` | | | ● | ● |
| `AuditRead` | | | ● | ● |
| `OrganizationDelete` | | | | ● |

Rules the matrix cannot express, enforced server-side:

- An admin may not act on an owner, nor grant the Owner role.
- Nobody may change their own role.
- An organization must always keep at least one owner.
- A member may update only tasks they created or are assigned to.
- A task's assignee must be a member of the same organization.

---

## Authentication

### `POST /api/auth/register`

Creates an account and signs it in. Does **not** create an organization.

```http
POST /api/auth/register
Content-Type: application/json

{ "email": "awadi@example.com", "password": "correct horse battery staple", "displayName": "Awadi" }
```

```json
{
  "accessToken": "eyJhbGciOi...",
  "tokenType": "Bearer",
  "expiresInSeconds": 900,
  "refreshToken": null,
  "user": { "id": "0199...", "email": "awadi@example.com", "displayName": "Awadi", "createdAt": "2026-10-26T09:00:00+00:00" }
}
```

Password rules: 12–128 characters, not a common guess, not containing your
email or name. `409` if the address is already registered; `422` with field
errors otherwise.

### `POST /api/auth/login`

Same response shape. Every failure — unknown account, wrong password, disabled,
locked — returns the same `401` and the same message, by design. Ten
consecutive failures lock the account for 15 minutes, which is not disclosed.

### `POST /api/auth/refresh`

Body optional; the cookie is used when it is absent.

```json
{ "refreshToken": "<only for non-browser clients>" }
```

Rotates the token. Replaying an already-rotated token revokes the whole chain
and returns `401` — the session ends, because a replayed token means it was
captured.

### `POST /api/auth/logout` → `204`

Revokes the whole rotation chain and clears the cookie. Succeeds either way, so
it cannot be used to test whether a token was valid.

### `GET /api/auth/me`

The caller and their organizations, each with the permissions their role grants
there. The frontend uses this to decide what to render; the server authorizes
every request regardless.

```json
{
  "user": { "id": "0199...", "email": "awadi@example.com", "displayName": "Awadi", "createdAt": "..." },
  "organizations": [
    {
      "organizationId": "0199...",
      "name": "Acme Corp",
      "slug": "acme-corp",
      "role": "Owner",
      "permissions": ["AuditRead", "MemberInvite", "..."],
      "joinedAt": "2026-10-26T09:00:00+00:00"
    }
  ]
}
```

---

## Organizations

| Endpoint | Permission | Notes |
| --- | --- | --- |
| `POST /api/organizations` | authenticated | Caller becomes Owner |
| `GET /api/organizations` | authenticated | The caller's memberships |
| `GET /api/organizations/{id}` | `OrganizationRead` | Includes caller role, permissions and counts |
| `PATCH /api/organizations/{id}` | `OrganizationUpdate` | Renames; the slug does not change |
| `DELETE /api/organizations/{id}?confirm={slug}` | `OrganizationDelete` | Irreversible |

```http
POST /api/organizations
{ "name": "Acme Corp" }
```

```json
{
  "id": "0199...", "name": "Acme Corp", "slug": "acme-corp",
  "callerRole": "Owner", "callerPermissions": ["..."],
  "memberCount": 1, "projectCount": 0, "createdAt": "..."
}
```

Deletion requires `?confirm=<slug>` to match exactly — an irreversible
whole-tenant operation should not be reachable by a mistyped URL. It cascades
to members, invitations, projects and tasks. The **audit trail survives**.

---

## Members

| Endpoint | Permission |
| --- | --- |
| `GET /api/organizations/{id}/members?search=&page=&pageSize=` | `MemberRead` |
| `PATCH /api/organizations/{id}/members/{userId}/role` | `MemberRoleUpdate` |
| `DELETE /api/organizations/{id}/members/{userId}` | `MemberRemove` |
| `POST /api/organizations/{id}/members/me/leave` | any member |

```http
PATCH /api/organizations/{id}/members/{userId}/role
{ "role": "Admin" }
```

`403` when an admin targets an owner, grants Owner, or anyone targets
themselves. `409` when the change would leave the organization without an
owner, or when the member already has that role.

Removing a member — or leaving — also clears that person's task assignments, so
no task is left assigned to someone who can no longer see it. The last owner
cannot leave: promote someone first.

---

## Invitations

| Endpoint | Permission |
| --- | --- |
| `GET /api/organizations/{id}/invitations?status=&page=` | `InvitationRead` |
| `POST /api/organizations/{id}/invitations` | `MemberInvite` |
| `POST /api/organizations/{id}/invitations/{invitationId}/resend` | `MemberInvite` |
| `DELETE /api/organizations/{id}/invitations/{invitationId}` | `InvitationRevoke` |
| `GET /api/invitations/preview?token=` | anonymous, rate limited |
| `POST /api/invitations/accept` | authenticated |

```http
POST /api/organizations/{id}/invitations
{ "email": "teammate@example.com", "role": "Member" }
```

```json
{
  "id": "0199...", "email": "teammate@example.com", "role": "Member",
  "status": "Pending", "deliveryStatus": "Pending", "deliveryAttempts": 0,
  "deliveryError": null, "expiresAt": "2026-11-02T09:00:00+00:00",
  "createdAt": "...", "acceptedAt": null
}
```

The request returns as soon as the invitation is stored; the **email is sent by
the background worker**. `deliveryStatus` and `deliveryError` let an admin see
that a send failed and resend.

**The token is never returned by the API.** It exists in the email and in the
queued job; the database holds only its SHA-256 hash.

`409` when the person is already a member, or an invitation is already pending
for that address. `403` when an admin tries to invite an Owner.

`POST .../resend` issues a **new** token, so the previous link stops working.
`DELETE` rotates the stored hash, so a link already sitting in an inbox stops
working immediately.

### Preview and accept

```http
GET /api/invitations/preview?token=<token>
```

```json
{
  "organizationName": "Acme Corp", "email": "teammate@example.com",
  "role": "Member", "expiresAt": "...", "isValid": true, "reason": null
}
```

Anonymous, so the accept screen can show who invited whom before sign-in, and
limited to those fields: no organization id, no member list, no inviter
address.

```http
POST /api/invitations/accept
{ "token": "<token>" }
```

The signed-in account's email must match the invited address (`403` otherwise),
so a forwarded link is useless to anyone else. Single use: a second attempt
returns `409`.

---

## Projects

| Endpoint | Permission |
| --- | --- |
| `GET /api/organizations/{id}/projects?search=&status=&page=` | `ProjectRead` |
| `GET /api/organizations/{id}/projects/{projectId}` | `ProjectRead` |
| `POST /api/organizations/{id}/projects` | `ProjectCreate` |
| `PUT /api/organizations/{id}/projects/{projectId}` | `ProjectUpdate` |
| `DELETE /api/organizations/{id}/projects/{projectId}` | `ProjectDelete` |

```http
POST /api/organizations/{id}/projects
{ "name": "Website Redesign", "description": "Q1 refresh" }
```

```json
{
  "id": "0199...", "name": "Website Redesign", "slug": "website-redesign",
  "description": "Q1 refresh", "status": "Active",
  "taskCount": 0, "openTaskCount": 0,
  "createdByUserId": "0199...", "createdAt": "...", "archivedAt": null
}
```

Slugs are unique **per organization**, so two tenants can both have
`website-redesign`; a duplicate within one organization gets a suffix. `PUT`
replaces name, description and status — setting `Archived` stamps `archivedAt`
and blocks new tasks (`409`). Deleting a project deletes its tasks.

---

## Tasks

| Endpoint | Permission |
| --- | --- |
| `GET /api/organizations/{id}/tasks?projectId=&status=&assigneeUserId=&search=&page=` | `TaskRead` |
| `GET /api/organizations/{id}/tasks/{taskId}` | `TaskRead` |
| `POST /api/organizations/{id}/projects/{projectId}/tasks` | `TaskCreate` |
| `PUT /api/organizations/{id}/tasks/{taskId}` | `TaskUpdateAny`, or `TaskUpdateOwn` for your own |
| `POST /api/organizations/{id}/tasks/{taskId}/assign` | `TaskAssign` |
| `DELETE /api/organizations/{id}/tasks/{taskId}` | `TaskDelete` |

```http
POST /api/organizations/{id}/projects/{projectId}/tasks
{ "title": "Draft the launch checklist", "description": null,
  "priority": "High", "assigneeUserId": null, "dueAt": null }
```

```http
PUT /api/organizations/{id}/tasks/{taskId}
{ "title": "Draft the launch checklist", "description": "Owner: marketing",
  "status": "Done", "priority": "High", "dueAt": null }
```

`completedAt` is derived from `status`, never accepted from the client: moving
to `Done` stamps it, moving away clears it.

Assignment is a separate operation so that "field omitted" and "clear the
assignee" cannot be confused:

```http
POST /api/organizations/{id}/tasks/{taskId}/assign
{ "assigneeUserId": "0199..." }   // null to unassign
```

`422` if the assignee is not a member of this organization. A member editing
someone else's task gets `403`; a project id from another tenant gets `404`.

---

## Audit

| Endpoint | Permission |
| --- | --- |
| `GET /api/organizations/{id}/audit-logs?action=&resourceType=&actorUserId=&from=&to=&page=` | `AuditRead` |
| `GET /api/organizations/{id}/audit-logs/actions` | `AuditRead` |

```json
{
  "items": [
    {
      "id": "0199...",
      "action": "member.role_changed",
      "resourceType": "membership",
      "resourceId": "0199...",
      "actorUserId": "0199...",
      "actorEmail": "awadi@example.com",
      "metadata": { "targetUserId": "0199...", "from": "Member", "to": "Admin" },
      "ipAddress": "172.18.0.1",
      "correlationId": "0199c0ffee7a4b1e9f2d",
      "createdAt": "2026-10-26T09:05:00+00:00"
    }
  ],
  "page": 1, "pageSize": 25, "totalCount": 37, "totalPages": 2, "hasNextPage": true
}
```

Read-only: there is no endpoint that writes, edits or deletes an entry.
Entries are written in the same transaction as the action they record, and the
database rejects `UPDATE`, `DELETE` and `TRUNCATE` on the table.

Account-level events (sign-in, failed sign-in, refresh replay) carry no
organization and never appear here; they are recorded for operators.

Recorded actions: `organization.created|updated|deleted`,
`member.invited|invitation_revoked|invitation_accepted|role_changed|removed|left`,
`project.created|updated|deleted`, `task.created|updated|assigned|deleted`, and
account-level `user.registered|logged_in|login_failed|logged_out`,
`token.refresh_reuse_detected`.

---

## Operations

| Endpoint | Purpose |
| --- | --- |
| `GET /health` | Liveness — no dependency checks, so a database blip never gets a healthy API restarted |
| `GET /health/ready` | Readiness — database (unhealthy if down) and Redis (degraded, since email is reconciled later) |
| `GET /openapi/v1.json` | OpenAPI document |
| `GET /docs` | Browsable reference |
