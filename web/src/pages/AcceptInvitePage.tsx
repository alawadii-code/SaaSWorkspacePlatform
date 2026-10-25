import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import type { AcceptInvitationResult, InvitationPreview } from '../api/types';
import { useAuth } from '../auth/AuthProvider';
import { CenteredLayout } from '../components/Layout';
import { Button, Card, ErrorBanner, RoleBadge, Spinner, formatDate } from '../components/ui';
import { useLoader } from '../hooks/useLoader';

/**
 * The screen an invitation link opens.
 *
 * The preview is anonymous, so the invitee can see who invited them before
 * signing in. Accepting requires an account whose email matches the invited
 * address — the server enforces that, and this page explains it when it
 * happens.
 */
export function AcceptInvitePage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token') ?? '';
  const { status, user, reload } = useAuth();
  const navigate = useNavigate();

  const [error, setError] = useState<unknown>(null);
  const [accepting, setAccepting] = useState(false);

  const preview = useLoader<InvitationPreview>(
    () => api.anonymous.get<InvitationPreview>(`/invitations/preview?token=${encodeURIComponent(token)}`),
    [token],
  );

  const accept = async () => {
    setAccepting(true);
    setError(null);

    try {
      const result = await api.post<AcceptInvitationResult>('/invitations/accept', { token });
      await reload();
      void navigate(`/orgs/${result.organizationId}`, { replace: true });
    } catch (failure) {
      setError(failure);
    } finally {
      setAccepting(false);
    }
  };

  if (!token) {
    return (
      <CenteredLayout>
        <Card>
          <h2 className="text-base font-semibold">That link is incomplete</h2>
          <p className="mt-2 text-sm text-[var(--color-muted)]">
            The invitation link is missing its token. Ask whoever invited you to resend it.
          </p>
        </Card>
      </CenteredLayout>
    );
  }

  if (preview.loading) {
    return (
      <CenteredLayout>
        <Card>
          <Spinner label="Checking your invitation" />
        </Card>
      </CenteredLayout>
    );
  }

  if (preview.error || !preview.data) {
    return (
      <CenteredLayout>
        <Card>
          <h2 className="text-base font-semibold">This invitation could not be found</h2>
          <p className="mt-2 text-sm text-[var(--color-muted)]">
            It may have been revoked or already used. Links stop working once they are redeemed.
          </p>
          <Link to="/login" className="mt-4 inline-block text-sm text-[var(--color-accent)] hover:underline">
            Go to sign in
          </Link>
        </Card>
      </CenteredLayout>
    );
  }

  const invitation = preview.data;
  const emailMatches = user?.email.toLowerCase() === invitation.email.toLowerCase();

  return (
    <CenteredLayout>
      <Card>
        <h2 className="text-base font-semibold">Join {invitation.organizationName}</h2>
        <p className="mt-1 text-sm text-[var(--color-muted)]">
          Invited as <RoleBadge role={invitation.role} /> &middot; {invitation.email}
        </p>

        <div className="mt-4">
          <ErrorBanner error={error} />
        </div>

        {!invitation.isValid ? (
          <p className="rounded-lg bg-[var(--color-surface)] px-3 py-2 text-sm">
            {invitation.reason ?? 'This invitation is no longer valid.'}
          </p>
        ) : status === 'anonymous' ? (
          <div className="space-y-3">
            <p className="text-sm">
              Sign in as <span className="font-medium">{invitation.email}</span> to accept, or create an
              account with that address.
            </p>
            <div className="flex gap-2">
              <Link to="/login" state={{ from: `/invitations/accept?token=${encodeURIComponent(token)}` }}>
                <Button variant="secondary">Sign in</Button>
              </Link>
              <Link
                to="/register"
                state={{ from: `/invitations/accept?token=${encodeURIComponent(token)}` }}
              >
                <Button>Create account</Button>
              </Link>
            </div>
          </div>
        ) : emailMatches ? (
          <div className="space-y-3">
            <p className="text-xs text-[var(--color-muted)]">
              This link works once and expires {formatDate(invitation.expiresAt)}.
            </p>
            <Button className="w-full" disabled={accepting} onClick={() => void accept()}>
              {accepting ? 'Joining…' : `Join ${invitation.organizationName}`}
            </Button>
          </div>
        ) : (
          <p className="rounded-lg bg-[var(--color-surface)] px-3 py-2 text-sm">
            This invitation was sent to <span className="font-medium">{invitation.email}</span>, but you
            are signed in as <span className="font-medium">{user?.email}</span>. Sign in with the invited
            address to accept it.
          </p>
        )}
      </Card>
    </CenteredLayout>
  );
}
