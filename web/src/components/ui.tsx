import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react';
import { ApiError } from '../api/client';
import type { InvitationStatus, OrganizationRole, TaskPriority, TaskState } from '../api/types';

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

const buttonStyles: Record<ButtonVariant, string> = {
  primary: 'bg-[var(--color-accent)] text-white hover:brightness-110 disabled:opacity-50',
  secondary:
    'bg-[var(--color-panel)] text-[var(--color-ink)] border border-[var(--color-line)] hover:bg-[var(--color-accent-soft)] disabled:opacity-50',
  ghost: 'text-[var(--color-muted)] hover:text-[var(--color-ink)] disabled:opacity-50',
  danger: 'bg-red-600 text-white hover:bg-red-700 disabled:opacity-50',
};

export function Button({
  variant = 'primary',
  className = '',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant }) {
  return (
    <button
      {...props}
      className={`inline-flex items-center justify-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition disabled:cursor-not-allowed ${buttonStyles[variant]} ${className}`}
    />
  );
}

export function Field({
  label,
  hint,
  error,
  children,
}: {
  label: string;
  hint?: string;
  error?: string | undefined;
  children: ReactNode;
}) {
  return (
    <label className="block space-y-1.5">
      <span className="block text-sm font-medium text-[var(--color-ink)]">{label}</span>
      {children}
      {hint && !error ? <span className="block text-xs text-[var(--color-muted)]">{hint}</span> : null}
      {error ? <span className="block text-xs text-red-600">{error}</span> : null}
    </label>
  );
}

export function Input({ className = '', ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      {...props}
      className={`w-full rounded-lg border border-[var(--color-line)] bg-[var(--color-panel)] px-3 py-2 text-sm outline-none placeholder:text-[var(--color-muted)] focus:border-[var(--color-accent)] ${className}`}
    />
  );
}

export function Select({ className = '', ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      {...props}
      className={`rounded-lg border border-[var(--color-line)] bg-[var(--color-panel)] px-3 py-2 text-sm outline-none focus:border-[var(--color-accent)] ${className}`}
    />
  );
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={`rounded-xl border border-[var(--color-line)] bg-[var(--color-panel)] p-5 ${className}`}
    >
      {children}
    </div>
  );
}

export function PageHeader({
  title,
  description,
  actions,
}: {
  title: string;
  description?: string;
  actions?: ReactNode;
}) {
  return (
    <header className="mb-6 flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        {description ? <p className="mt-1 text-sm text-[var(--color-muted)]">{description}</p> : null}
      </div>
      {actions ? <div className="flex items-center gap-2">{actions}</div> : null}
    </header>
  );
}

export function EmptyState({ title, description }: { title: string; description?: string }) {
  return (
    <div className="rounded-xl border border-dashed border-[var(--color-line)] px-6 py-10 text-center">
      <p className="font-medium">{title}</p>
      {description ? <p className="mt-1 text-sm text-[var(--color-muted)]">{description}</p> : null}
    </div>
  );
}

/**
 * Shows an API failure in the terms the server used lol sOoo problem documents carry a
 * human-readable detail, so there is no need to invent a message.
 */
export function ErrorBanner({ error }: { error: unknown }) {
  if (!error) {
    return null;
  }

  const message =
    error instanceof ApiError
      ? error.message
      : error instanceof Error
        ? error.message
        : 'Something went wrong.';

  const code = error instanceof ApiError ? error.code : undefined;

  return (
    <div
      role="alert"
      className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800"
    >
      {message}
      {code ? <span className="ml-2 font-mono text-xs opacity-70">({code})</span> : null}
    </div>
  );
}

export function Spinner({ label = 'Loading' }: { label?: string }) {
  return (
    <div className="flex items-center gap-2 py-8 text-sm text-[var(--color-muted)]">
      <span
        aria-hidden="true"
        className="size-4 animate-spin rounded-full border-2 border-[var(--color-line)] border-t-[var(--color-accent)]"
      />
      {label}…
    </div>
  );
}

const roleStyles: Record<OrganizationRole, string> = {
  Owner: 'bg-amber-100 text-amber-900',
  Admin: 'bg-[var(--color-accent-soft)] text-[var(--color-accent)]',
  Member: 'bg-emerald-50 text-emerald-800',
  Viewer: 'bg-slate-100 text-slate-700',
};

const taskStateStyles: Record<TaskState, string> = {
  Todo: 'bg-slate-100 text-slate-700',
  InProgress: 'bg-[var(--color-accent-soft)] text-[var(--color-accent)]',
  Blocked: 'bg-red-50 text-red-700',
  Done: 'bg-emerald-50 text-emerald-800',
};

const priorityStyles: Record<TaskPriority, string> = {
  Low: 'text-slate-500',
  Medium: 'text-slate-700',
  High: 'text-amber-700',
  Urgent: 'text-red-700',
};

const invitationStyles: Record<InvitationStatus, string> = {
  Pending: 'bg-amber-50 text-amber-800',
  Accepted: 'bg-emerald-50 text-emerald-800',
  Revoked: 'bg-slate-100 text-slate-600',
  Expired: 'bg-slate-100 text-slate-600',
};

export function Badge({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${className}`}
    >
      {children}
    </span>
  );
}

export const RoleBadge = ({ role }: { role: OrganizationRole }) => (
  <Badge className={roleStyles[role]}>{role}</Badge>
);

export const TaskStateBadge = ({ status }: { status: TaskState }) => (
  <Badge className={taskStateStyles[status]}>{status === 'InProgress' ? 'In progress' : status}</Badge>
);

export const PriorityLabel = ({ priority }: { priority: TaskPriority }) => (
  <span className={`text-xs font-medium ${priorityStyles[priority]}`}>{priority}</span>
);

export const InvitationBadge = ({ status }: { status: InvitationStatus }) => (
  <Badge className={invitationStyles[status]}>{status}</Badge>
);

/** Short, locale-aware timestamp. */
export function formatDate(value: string): string {
  return new Date(value).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
}
