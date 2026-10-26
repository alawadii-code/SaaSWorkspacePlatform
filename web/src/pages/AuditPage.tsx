import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { AuditEntry, Paged } from '../api/types';
import {
  Badge,
  Button,
  EmptyState,
  ErrorBanner,
  PageHeader,
  Select,
  Spinner,
  formatDate,
} from '../components/ui';
import { useLoader } from '../hooks/useLoader';

/**
 * the organization's audit trail: read-only by design. There is no API path
 * that edits or deletes an entry, and the database rejects both.
 */
export function AuditPage() {
  const { organizationId } = useParams<{ organizationId: string }>();

  const [action, setAction] = useState('');
  const [page, setPage] = useState(1);

  const actions = useLoader<string[]>(
    () => api.get<string[]>(`/organizations/${organizationId}/audit-logs/actions`),
    [organizationId],
  );

  const entries = useLoader<Paged<AuditEntry>>(
    () =>
      api.get<Paged<AuditEntry>>(
        `/organizations/${organizationId}/audit-logs?page=${page}&pageSize=25` +
          (action === '' ? '' : `&action=${encodeURIComponent(action)}`),
      ),
    [organizationId, action, page],
  );

  return (
    <div>
      <PageHeader
        title="Audit log"
        description="Every change, with who made it and when. Append-only."
        actions={
          <Select
            aria-label="Filter by action"
            value={action}
            onChange={(event) => {
              setAction(event.target.value);
              setPage(1);
            }}
          >
            <option value="">All actions</option>
            {(actions.data ?? []).map((candidate) => (
              <option key={candidate} value={candidate}>
                {candidate}
              </option>
            ))}
          </Select>
        }
      />

      <ErrorBanner error={entries.error} />

      {entries.loading ? (
        <Spinner label="Loading audit entries" />
      ) : entries.data && entries.data.items.length > 0 ? (
        <>
          <div className="space-y-2">
            {entries.data.items.map((entry) => (
              <div
                key={entry.id}
                className="rounded-xl border border-[var(--color-line)] bg-[var(--color-panel)] p-4"
              >
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <div className="flex items-center gap-2">
                    <Badge className="bg-[var(--color-accent-soft)] font-mono text-[var(--color-accent)]">
                      {entry.action}
                    </Badge>
                    <span className="text-sm text-[var(--color-muted)]">{entry.resourceType}</span>
                  </div>
                  <span className="text-xs text-[var(--color-muted)]">{formatDate(entry.createdAt)}</span>
                </div>

                <p className="mt-2 text-sm">
                  {entry.actorEmail ?? 'System'}
                  {entry.ipAddress ? (
                    <span className="text-[var(--color-muted)]"> · {entry.ipAddress}</span>
                  ) : null}
                </p>

                {entry.metadata ? (
                  <details className="mt-2">
                    <summary className="cursor-pointer text-xs text-[var(--color-muted)]">
                      Details
                    </summary>
                    <pre className="mt-2 overflow-x-auto rounded-lg bg-[var(--color-surface)] p-3 text-xs">
                      {JSON.stringify(entry.metadata, null, 2)}
                    </pre>
                  </details>
                ) : null}
              </div>
            ))}
          </div>

          <div className="mt-4 flex items-center justify-between text-sm text-[var(--color-muted)]">
            <span>
              Page {entries.data.page} of {Math.max(entries.data.totalPages, 1)} ·{' '}
              {entries.data.totalCount} entries
            </span>
            <div className="flex gap-2">
              <Button
                variant="secondary"
                disabled={entries.data.page <= 1}
                onClick={() => setPage((current) => Math.max(current - 1, 1))}
              >
                Previous
              </Button>
              <Button
                variant="secondary"
                disabled={!entries.data.hasNextPage}
                onClick={() => setPage((current) => current + 1)}
              >
                Next
              </Button>
            </div>
          </div>
        </>
      ) : (
        <EmptyState title="Nothing recorded yet" description="Actions appear here as they happen." />
      )}
    </div>
  );
}
