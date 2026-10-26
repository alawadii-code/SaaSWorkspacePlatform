import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { Member, Paged, Project, TaskPriority, TaskState, WorkTask } from '../api/types';
import { useMembership } from '../auth/AuthProvider';
import {
  Button,
  Card,
  EmptyState,
  ErrorBanner,
  Field,
  Input,
  PageHeader,
  PriorityLabel,
  Select,
  Spinner,
  TaskStateBadge,
  formatDate,
} from '../components/ui';
import { useLoader } from '../hooks/useLoader';

const taskStates: TaskState[] = ['Todo', 'InProgress', 'Blocked', 'Done'];
const priorities: TaskPriority[] = ['Low', 'Medium', 'High', 'Urgent'];

export function ProjectPage() {
  const { organizationId, projectId } = useParams<{ organizationId: string; projectId: string }>();
  const { can } = useMembership(organizationId);

  const [title, setTitle] = useState('');
  const [priority, setPriority] = useState<TaskPriority>('Medium');
  const [assigneeUserId, setAssigneeUserId] = useState('');
  const [mutationError, setMutationError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  const project = useLoader<Project>(
    () => api.get<Project>(`/organizations/${organizationId}/projects/${projectId}`),
    [organizationId, projectId],
  );

  const tasks = useLoader<Paged<WorkTask>>(
    () =>
      api.get<Paged<WorkTask>>(
        `/organizations/${organizationId}/tasks?projectId=${projectId}&pageSize=100`,
      ),
    [organizationId, projectId],
  );

  // Needed to offer assignees. A viewer can read members, so this is safe to
  // load for evry role that can see the board.
  const members = useLoader<Paged<Member>>(
    () => api.get<Paged<Member>>(`/organizations/${organizationId}/members?pageSize=100`),
    [organizationId],
  );

  const createTask = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setMutationError(null);

    try {
      await api.post(`/organizations/${organizationId}/projects/${projectId}/tasks`, {
        title,
        description: null,
        priority,
        assigneeUserId: assigneeUserId === '' ? null : assigneeUserId,
        dueAt: null,
      });

      setTitle('');
      setAssigneeUserId('');
      tasks.reload();
      project.reload();
    } catch (failure) {
      setMutationError(failure);
    } finally {
      setBusy(false);
    }
  };

  /** Status and priority go through the full-replacement update endpoint. */
  const updateTask = async (task: WorkTask, changes: Partial<Pick<WorkTask, 'status' | 'priority'>>) => {
    setMutationError(null);

    try {
      await api.put(`/organizations/${organizationId}/tasks/${task.id}`, {
        title: task.title,
        description: task.description,
        status: changes.status ?? task.status,
        priority: changes.priority ?? task.priority,
        dueAt: task.dueAt,
      });

      tasks.reload();
      project.reload();
    } catch (failure) {
      setMutationError(failure);
    }
  };

  const assign = async (task: WorkTask, userId: string) => {
    setMutationError(null);

    try {
      await api.post(`/organizations/${organizationId}/tasks/${task.id}/assign`, {
        assigneeUserId: userId === '' ? null : userId,
      });

      tasks.reload();
    } catch (failure) {
      setMutationError(failure);
    }
  };

  const removeTask = async (task: WorkTask) => {
    setMutationError(null);

    try {
      await api.delete(`/organizations/${organizationId}/tasks/${task.id}`);
      tasks.reload();
      project.reload();
    } catch (failure) {
      setMutationError(failure);
    }
  };

  if (project.loading) {
    return <Spinner label="Loading project" />;
  }

  if (project.error || !project.data) {
    return (
      <div>
        <ErrorBanner error={project.error ?? new Error('Project not found.')} />
        <Link to={`/orgs/${organizationId}`} className="text-sm text-[var(--color-accent)] hover:underline">
          Back to projects
        </Link>
      </div>
    );
  }

  const memberOptions = members.data?.items ?? [];
  const isArchived = project.data.status === 'Archived';

  return (
    <div>
      <PageHeader
        title={project.data.name}
        description={project.data.description ?? undefined}
        actions={
          <Link
            to={`/orgs/${organizationId}`}
            className="text-sm text-[var(--color-muted)] hover:text-[var(--color-ink)]"
          >
            All projects
          </Link>
        }
      />

      <ErrorBanner error={mutationError ?? tasks.error} />

      {tasks.loading ? (
        <Spinner label="Loading tasks" />
      ) : tasks.data && tasks.data.items.length > 0 ? (
        <div className="overflow-hidden rounded-xl border border-[var(--color-line)] bg-[var(--color-panel)]">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-[var(--color-line)] text-xs uppercase tracking-wide text-[var(--color-muted)]">
              <tr>
                <th className="px-4 py-3 font-medium">Task</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium">Assignee</th>
                <th className="px-4 py-3 font-medium">Priority</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {tasks.data.items.map((task) => (
                <tr key={task.id} className="border-b border-[var(--color-line)] last:border-b-0">
                  <td className="px-4 py-3">
                    <p className="font-medium">{task.title}</p>
                    <p className="text-xs text-[var(--color-muted)]">
                      Created {formatDate(task.createdAt)}
                      {task.completedAt ? ` · Completed ${formatDate(task.completedAt)}` : ''}
                    </p>
                  </td>
                  <td className="px-4 py-3">
                    <Select
                      aria-label={`Status of ${task.title}`}
                      value={task.status}
                      onChange={(event) =>
                        void updateTask(task, { status: event.target.value as TaskState })
                      }
                    >
                      {taskStates.map((state) => (
                        <option key={state} value={state}>
                          {state === 'InProgress' ? 'In progress' : state}
                        </option>
                      ))}
                    </Select>
                  </td>
                  <td className="px-4 py-3">
                    {can('TaskAssign') ? (
                      <Select
                        aria-label={`Assignee of ${task.title}`}
                        value={task.assigneeUserId ?? ''}
                        onChange={(event) => void assign(task, event.target.value)}
                      >
                        <option value="">Unassigned</option>
                        {memberOptions.map((member) => (
                          <option key={member.userId} value={member.userId}>
                            {member.displayName}
                          </option>
                        ))}
                      </Select>
                    ) : (
                      <span className="text-[var(--color-muted)]">
                        {task.assigneeName ?? 'Unassigned'}
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-2">
                      <TaskStateBadge status={task.status} />
                      <PriorityLabel priority={task.priority} />
                    </div>
                  </td>
                  <td className="px-4 py-3 text-right">
                    {can('TaskDelete') ? (
                      <Button variant="ghost" onClick={() => void removeTask(task)}>
                        Delete
                      </Button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <EmptyState title="No tasks in this project yet" />
      )}

      {can('TaskCreate') && !isArchived ? (
        <Card className="mt-8">
          <h2 className="mb-3 text-sm font-semibold">New task</h2>
          <form
            className="flex flex-wrap items-end gap-3"
            onSubmit={(event) => void createTask(event)}
          >
            <div className="min-w-56 flex-1">
              <Field label="Title">
                <Input
                  required
                  minLength={2}
                  maxLength={200}
                  placeholder="Draft the launch checklist"
                  value={title}
                  onChange={(event) => setTitle(event.target.value)}
                />
              </Field>
            </div>
            <Field label="Priority">
              <Select value={priority} onChange={(event) => setPriority(event.target.value as TaskPriority)}>
                {priorities.map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Assignee">
              <Select
                value={assigneeUserId}
                onChange={(event) => setAssigneeUserId(event.target.value)}
              >
                <option value="">Unassigned</option>
                {memberOptions.map((member) => (
                  <option key={member.userId} value={member.userId}>
                    {member.displayName}
                  </option>
                ))}
              </Select>
            </Field>
            <Button type="submit" disabled={busy}>
              {busy ? 'Adding…' : 'Add task'}
            </Button>
          </form>
        </Card>
      ) : null}

      {isArchived ? (
        <p className="mt-6 text-sm text-[var(--color-muted)]">
          This project is archived, so no new tasks can be added to it.
        </p>
      ) : null}
    </div>
  );
}
