import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { Invitation, Member, OrganizationRole, Paged } from '../api/types';
import { useAuth, useMembership } from '../auth/AuthProvider';
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorBanner,
  Field,
  Input,
  InvitationBadge,
  PageHeader,
  RoleBadge,
  Select,
  Spinner,
  formatDate,
} from '../components/ui';
import { useLoader } from '../hooks/useLoader';

const assignableRoles: OrganizationRole[] = ['Viewer', 'Member', 'Admin', 'Owner'];

export function MembersPage() {
  const { organizationId } = useParams<{ organizationId: string }>();
  const { can, role } = useMembership(organizationId);
  const { reload: reloadSession } = useAuth();
  const navigate = useNavigate();

  const [inviteEmail, setInviteEmail] = useState('');
  const [inviteRole, setInviteRole] = useState<OrganizationRole>('Member');
  const [mutationError, setMutationError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const members = useLoader<Paged<Member>>(
    () => api.get<Paged<Member>>(`/organizations/${organizationId}/members?pageSize=100`),
    [organizationId],
  );

  // Only admins and above may read invitations, so this request is skipped for
  // everyone else rather than firing and being refused.
  const invitations = useLoader<Paged<Invitation> | null>(
    () =>
      can('InvitationRead')
        ? api.get<Paged<Invitation>>(`/organizations/${organizationId}/invitations?pageSize=100`)
        : Promise.resolve(null),
    [organizationId, can('InvitationRead')],
  );

  // An owner can only grant Owner; an admin cannot.
  const grantableRoles = assignableRoles.filter((candidate) =>
    candidate === 'Owner' ? role === 'Owner' : true,
  );

  const invite = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setMutationError(null);
    setNotice(null);

    try {
      await api.post(`/organizations/${organizationId}/invitations`, {
        email: inviteEmail,
        role: inviteRole,
      });

      setInviteEmail('');
      setNotice('Invitation queued. The email is sent by the background worker.');
      invitations.reload();
    } catch (failure) {
      setMutationError(failure);
    } finally {
      setBusy(false);
    }
  };

  const changeRole = async (member: Member, next: OrganizationRole) => {
    setMutationError(null);
    setNotice(null);

    try {
      await api.patch(`/organizations/${organizationId}/members/${member.userId}/role`, {
        role: next,
      });

      members.reload();
    } catch (failure) {
      setMutationError(failure);
    }
  };

  const removeMember = async (member: Member) => {
    setMutationError(null);

    try {
      await api.delete(`/organizations/${organizationId}/members/${member.userId}`);
      members.reload();
    } catch (failure) {
      setMutationError(failure);
    }
  };

  const leave = async () => {
    setMutationError(null);

    try {
      await api.post(`/organizations/${organizationId}/members/me/leave`);
      await reloadSession();
      void navigate('/orgs', { replace: true });
    } catch (failure) {
      setMutationError(failure);
    }
  };

  const resend = async (invitation: Invitation) => {
    setMutationError(null);
    setNotice(null);

    try {
      await api.post(`/organizations/${organizationId}/invitations/${invitation.id}/resend`);
      setNotice('A new link was queued. The previous one no longer works.');
      invitations.reload();
    } catch (failure) {
      setMutationError(failure);
    }
  };

  const revoke = async (invitation: Invitation) => {
    setMutationError(null);

    try {
      await api.delete(`/organizations/${organizationId}/invitations/${invitation.id}`);
      invitations.reload();
    } catch (failure) {
      setMutationError(failure);
    }
  };

  return (
    <div>
      <PageHeader
        title="People"
        description={members.data ? `${members.data.totalCount} member(s)` : undefined}
        actions={
          <Button variant="ghost" onClick={() => void leave()}>
            Leave organization
          </Button>
        }
      />

      <ErrorBanner error={mutationError ?? members.error} />

      {notice ? (
        <p className="mb-4 rounded-lg border border-[var(--color-line)] bg-[var(--color-accent-soft)] px-4 py-3 text-sm text-[var(--color-accent)]">
          {notice}
        </p>
      ) : null}

      {members.loading ? (
        <Spinner label="Loading members" />
      ) : (
        <div className="overflow-hidden rounded-xl border border-[var(--color-line)] bg-[var(--color-panel)]">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-[var(--color-line)] text-xs uppercase tracking-wide text-[var(--color-muted)]">
              <tr>
                <th className="px-4 py-3 font-medium">Person</th>
                <th className="px-4 py-3 font-medium">Role</th>
                <th className="px-4 py-3 font-medium">Joined</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {(members.data?.items ?? []).map((member) => (
                <tr key={member.membershipId} className="border-b border-[var(--color-line)] last:border-b-0">
                  <td className="px-4 py-3">
                    <p className="font-medium">
                      {member.displayName}
                      {member.isYou ? (
                        <Badge className="ml-2 bg-[var(--color-surface)] text-[var(--color-muted)]">you</Badge>
                      ) : null}
                    </p>
                    <p className="text-xs text-[var(--color-muted)]">{member.email}</p>
                  </td>
                  <td className="px-4 py-3">
                    {can('MemberRoleUpdate') && !member.isYou ? (
                      <Select
                        aria-label={`Role of ${member.displayName}`}
                        value={member.role}
                        onChange={(event) =>
                          void changeRole(member, event.target.value as OrganizationRole)
                        }
                      >
                        {grantableRoles.map((candidate) => (
                          <option key={candidate} value={candidate}>
                            {candidate}
                          </option>
                        ))}
                      </Select>
                    ) : (
                      <RoleBadge role={member.role} />
                    )}
                  </td>
                  <td className="px-4 py-3 text-[var(--color-muted)]">{formatDate(member.joinedAt)}</td>
                  <td className="px-4 py-3 text-right">
                    {can('MemberRemove') && !member.isYou ? (
                      <Button variant="ghost" onClick={() => void removeMember(member)}>
                        Remove
                      </Button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {can('MemberInvite') ? (
        <Card className="mt-8">
          <h2 className="mb-3 text-sm font-semibold">Invite someone</h2>
          <form className="flex flex-wrap items-end gap-3" onSubmit={(event) => void invite(event)}>
            <div className="min-w-56 flex-1">
              <Field label="Email">
                <Input
                  type="email"
                  required
                  maxLength={254}
                  placeholder="colleague@example.com"
                  value={inviteEmail}
                  onChange={(event) => setInviteEmail(event.target.value)}
                />
              </Field>
            </div>
            <Field label="Role">
              <Select
                value={inviteRole}
                onChange={(event) => setInviteRole(event.target.value as OrganizationRole)}
              >
                {grantableRoles.map((candidate) => (
                  <option key={candidate} value={candidate}>
                    {candidate}
                  </option>
                ))}
              </Select>
            </Field>
            <Button type="submit" disabled={busy}>
              {busy ? 'Queueing…' : 'Send invitation'}
            </Button>
          </form>
        </Card>
      ) : null}

      {can('InvitationRead') ? (
        <section className="mt-8">
          <h2 className="mb-3 text-sm font-semibold">Invitations</h2>

          {invitations.loading ? (
            <Spinner label="Loading invitations" />
          ) : invitations.data && invitations.data.items.length > 0 ? (
            <div className="overflow-hidden rounded-xl border border-[var(--color-line)] bg-[var(--color-panel)]">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-[var(--color-line)] text-xs uppercase tracking-wide text-[var(--color-muted)]">
                  <tr>
                    <th className="px-4 py-3 font-medium">Email</th>
                    <th className="px-4 py-3 font-medium">Role</th>
                    <th className="px-4 py-3 font-medium">Status</th>
                    <th className="px-4 py-3 font-medium">Delivery</th>
                    <th className="px-4 py-3" />
                  </tr>
                </thead>
                <tbody>
                  {invitations.data.items.map((invitation) => (
                    <tr
                      key={invitation.id}
                      className="border-b border-[var(--color-line)] last:border-b-0"
                    >
                      <td className="px-4 py-3">
                        <p>{invitation.email}</p>
                        <p className="text-xs text-[var(--color-muted)]">
                          Expires {formatDate(invitation.expiresAt)}
                        </p>
                      </td>
                      <td className="px-4 py-3">
                        <RoleBadge role={invitation.role} />
                      </td>
                      <td className="px-4 py-3">
                        <InvitationBadge status={invitation.status} />
                      </td>
                      <td className="px-4 py-3">
                        <p className="text-xs">
                          {invitation.deliveryStatus}
                          {invitation.deliveryAttempts > 0
                            ? ` · ${invitation.deliveryAttempts} attempt(s)`
                            : ''}
                        </p>
                        {invitation.deliveryError ? (
                          <p className="mt-0.5 max-w-64 truncate text-xs text-red-600">
                            {invitation.deliveryError}
                          </p>
                        ) : null}
                      </td>
                      <td className="px-4 py-3 text-right">
                        {invitation.status === 'Pending' ? (
                          <div className="flex justify-end gap-1">
                            {can('MemberInvite') ? (
                              <Button variant="ghost" onClick={() => void resend(invitation)}>
                                Resend
                              </Button>
                            ) : null}
                            {can('InvitationRevoke') ? (
                              <Button variant="ghost" onClick={() => void revoke(invitation)}>
                                Revoke
                              </Button>
                            ) : null}
                          </div>
                        ) : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <EmptyState title="No invitations" description="Invite someone to see it listed here." />
          )}
        </section>
      ) : null}
    </div>
  );
}
