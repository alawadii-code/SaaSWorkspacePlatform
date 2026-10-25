import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { Organization } from '../api/types';
import { useAuth } from '../auth/AuthProvider';
import {
  Button,
  Card,
  EmptyState,
  ErrorBanner,
  Field,
  Input,
  PageHeader,
  RoleBadge,
} from '../components/ui';

export function OrganizationsPage() {
  const { organizations, user, logout, reload } = useAuth();
  const navigate = useNavigate();

  const [name, setName] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [creating, setCreating] = useState(false);

  const create = async (event: React.FormEvent) => {
    event.preventDefault();
    setCreating(true);
    setError(null);

    try {
      const organization = await api.post<Organization>('/organizations', { name });
      setName('');
      await reload();
      void navigate(`/orgs/${organization.id}`);
    } catch (failure) {
      setError(failure);
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="mx-auto w-full max-w-3xl p-4 md:p-8">
      <PageHeader
        title="Your organizations"
        description={user?.email}
        actions={
          <Button variant="ghost" onClick={() => void logout()}>
            Sign out
          </Button>
        }
      />

      <ErrorBanner error={error} />

      <div className="space-y-3">
        {organizations.length === 0 ? (
          <EmptyState
            title="You are not in an organization yet"
            description="Create one below, or accept an invitation from someone who already has one."
          />
        ) : (
          organizations.map((membership) => (
            <Link
              key={membership.organizationId}
              to={`/orgs/${membership.organizationId}`}
              className="block rounded-xl border border-[var(--color-line)] bg-[var(--color-panel)] p-4 transition hover:border-[var(--color-accent)]"
            >
              <div className="flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <p className="truncate font-medium">{membership.name}</p>
                  <p className="font-mono text-xs text-[var(--color-muted)]">{membership.slug}</p>
                </div>
                <RoleBadge role={membership.role} />
              </div>
            </Link>
          ))
        )}
      </div>

      <Card className="mt-8">
        <h2 className="mb-3 text-sm font-semibold">Create an organization</h2>
        <form className="flex flex-wrap items-end gap-3" onSubmit={(event) => void create(event)}>
          <div className="min-w-56 flex-1">
            <Field label="Name">
              <Input
                required
                minLength={2}
                maxLength={100}
                placeholder="Acme Corp"
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            </Field>
          </div>
          <Button type="submit" disabled={creating}>
            {creating ? 'Creating…' : 'Create'}
          </Button>
        </form>
        <p className="mt-3 text-xs text-[var(--color-muted)]">
          You become its owner, and can invite people once it exists.
        </p>
      </Card>
    </div>
  );
}
