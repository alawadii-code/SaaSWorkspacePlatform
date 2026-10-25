import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { Paged, Project } from '../api/types';
import { useMembership } from '../auth/AuthProvider';
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorBanner,
  Field,
  Input,
  PageHeader,
  Spinner,
} from '../components/ui';
import { useLoader } from '../hooks/useLoader';

export function ProjectsPage() {
  const { organizationId } = useParams<{ organizationId: string }>();
  const { can } = useMembership(organizationId);

  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [mutationError, setMutationError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  const projects = useLoader<Paged<Project>>(
    () => api.get<Paged<Project>>(`/organizations/${organizationId}/projects?pageSize=100`),
    [organizationId],
  );

  const create = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setMutationError(null);

    try {
      await api.post(`/organizations/${organizationId}/projects`, {
        name,
        description: description.trim() === '' ? null : description,
      });

      setName('');
      setDescription('');
      projects.reload();
    } catch (failure) {
      setMutationError(failure);
    } finally {
      setBusy(false);
    }
  };

  const setArchived = async (project: Project, archived: boolean) => {
    setMutationError(null);

    try {
      await api.put(`/organizations/${organizationId}/projects/${project.id}`, {
        name: project.name,
        description: project.description,
        status: archived ? 'Archived' : 'Active',
      });

      projects.reload();
    } catch (failure) {
      setMutationError(failure);
    }
  };

  const remove = async (project: Project) => {
    setMutationError(null);

    try {
      await api.delete(`/organizations/${organizationId}/projects/${project.id}`);
      projects.reload();
    } catch (failure) {
      setMutationError(failure);
    }
  };

  return (
    <div>
      <PageHeader
        title="Projects"
        description={
          projects.data ? `${projects.data.totalCount} in this organization` : undefined
        }
      />

      <ErrorBanner error={mutationError ?? projects.error} />

      {projects.loading ? (
        <Spinner label="Loading projects" />
      ) : projects.data && projects.data.items.length > 0 ? (
        <div className="space-y-3">
          {projects.data.items.map((project) => (
            <div
              key={project.id}
              className="rounded-xl border border-[var(--color-line)] bg-[var(--color-panel)] p-4"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <Link
                      to={`/orgs/${organizationId}/projects/${project.id}`}
                      className="truncate font-medium hover:text-[var(--color-accent)] hover:underline"
                    >
                      {project.name}
                    </Link>
                    {project.status === 'Archived' ? (
                      <Badge className="bg-slate-100 text-slate-600">Archived</Badge>
                    ) : null}
                  </div>
                  {project.description ? (
                    <p className="mt-1 line-clamp-2 text-sm text-[var(--color-muted)]">
                      {project.description}
                    </p>
                  ) : null}
                  <p className="mt-1.5 text-xs text-[var(--color-muted)]">
                    {project.openTaskCount} open of {project.taskCount} task
                    {project.taskCount === 1 ? '' : 's'}
                  </p>
                </div>

                <div className="flex shrink-0 items-center gap-2">
                  {can('ProjectUpdate') ? (
                    <Button
                      variant="secondary"
                      onClick={() => void setArchived(project, project.status === 'Active')}
                    >
                      {project.status === 'Active' ? 'Archive' : 'Restore'}
                    </Button>
                  ) : null}
                  {can('ProjectDelete') ? (
                    <Button variant="ghost" onClick={() => void remove(project)}>
                      Delete
                    </Button>
                  ) : null}
                </div>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <EmptyState
          title="No projects yet"
          description={
            can('ProjectCreate')
              ? 'Create the first one below.'
              : 'An admin or owner can create the first one.'
          }
        />
      )}

      {can('ProjectCreate') ? (
        <Card className="mt-8">
          <h2 className="mb-3 text-sm font-semibold">New project</h2>
          <form className="space-y-3" onSubmit={(event) => void create(event)}>
            <Field label="Name">
              <Input
                required
                minLength={2}
                maxLength={120}
                placeholder="Website redesign"
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            </Field>
            <Field label="Description" hint="Optional.">
              <Input
                maxLength={2000}
                value={description}
                onChange={(event) => setDescription(event.target.value)}
              />
            </Field>
            <Button type="submit" disabled={busy}>
              {busy ? 'Creating…' : 'Create project'}
            </Button>
          </form>
        </Card>
      ) : null}
    </div>
  );
}
