import { NavLink, Navigate, Outlet, useLocation, useParams } from 'react-router-dom';
import { useAuth, useMembership } from '../auth/AuthProvider';
import { Button, RoleBadge, Spinner } from './ui';

/** Gate for every signed-in route. */
export function RequireAuth() {
  const { status } = useAuth();
  const location = useLocation();

  if (status === 'loading') {
    return (
      <div className="mx-auto max-w-md p-10">
        <Spinner label="Restoring your session" />
      </div>
    );
  }

  if (status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }

  return <Outlet />;
}

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `block rounded-lg px-3 py-2 text-sm font-medium transition ${
    isActive
      ? 'bg-[var(--color-accent-soft)] text-[var(--color-accent)]'
      : 'text-[var(--color-muted)] hover:bg-[var(--color-surface)] hover:text-[var(--color-ink)]'
  }`;

/**
 * Shell for the organization-scoped screens.
 *
 * Navigation is filtered by the caller's permissions, which is presentation
 * only: the audit link is hidden from a member because it would 403, not
 * because hiding it protects anything.
 */
export function OrganizationLayout() {
  const { organizationId } = useParams<{ organizationId: string }>();
  const { user, organizations, logout } = useAuth();
  const { membership, can } = useMembership(organizationId);

  if (!membership) {
    // Either the id is wrong or the caller is no longer a member; the
    // organization list is the honest place to land.
    return <Navigate to="/orgs" replace />;
  }

  return (
    <div className="mx-auto flex min-h-dvh w-full max-w-6xl flex-col gap-6 p-4 md:flex-row md:p-8">
      <aside className="w-full shrink-0 md:w-60">
        <div className="mb-6">
          <NavLink to="/orgs" className="text-xs font-medium text-[var(--color-muted)] hover:underline">
            ← All organizations
          </NavLink>
          <h2 className="mt-2 text-base font-semibold leading-tight">{membership.name}</h2>
          <div className="mt-1.5 flex items-center gap-2">
            <RoleBadge role={membership.role} />
            <span className="font-mono text-xs text-[var(--color-muted)]">{membership.slug}</span>
          </div>
        </div>

        <nav className="space-y-1">
          <NavLink end to={`/orgs/${organizationId}`} className={navLinkClass}>
            Projects
          </NavLink>
          <NavLink to={`/orgs/${organizationId}/members`} className={navLinkClass}>
            People
          </NavLink>
          {can('AuditRead') ? (
            <NavLink to={`/orgs/${organizationId}/audit`} className={navLinkClass}>
              Audit log
            </NavLink>
          ) : null}
        </nav>

        {organizations.length > 1 ? (
          <div className="mt-6 border-t border-[var(--color-line)] pt-4">
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--color-muted)]">
              Switch
            </p>
            <div className="space-y-1">
              {organizations
                .filter((candidate) => candidate.organizationId !== organizationId)
                .map((candidate) => (
                  <NavLink
                    key={candidate.organizationId}
                    to={`/orgs/${candidate.organizationId}`}
                    className={navLinkClass}
                  >
                    {candidate.name}
                  </NavLink>
                ))}
            </div>
          </div>
        ) : null}

        <div className="mt-6 border-t border-[var(--color-line)] pt-4">
          <p className="truncate text-xs text-[var(--color-muted)]">{user?.email}</p>
          <Button variant="ghost" className="mt-1 px-0" onClick={() => void logout()}>
            Sign out
          </Button>
        </div>
      </aside>

      <main className="min-w-0 flex-1">
        <Outlet />
      </main>
    </div>
  );
}

/** Centered shell for the sign-in, registration and invitation screens. */
export function CenteredLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-dvh items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <div className="mb-6 text-center">
          <h1 className="text-lg font-semibold tracking-tight">Workspace</h1>
          <p className="text-sm text-[var(--color-muted)]">Projects, people and an audit trail.</p>
        </div>
        {children}
      </div>
    </div>
  );
}
