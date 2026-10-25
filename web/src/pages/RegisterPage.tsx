import { useState } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthProvider';
import { CenteredLayout } from '../components/Layout';
import { Button, Card, ErrorBanner, Field, Input } from '../components/ui';

export function RegisterPage() {
  const { status, register } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();

  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  const from = (location.state as { from?: string } | null)?.from ?? '/orgs';

  if (status === 'authenticated') {
    return <Navigate to={from} replace />;
  }

  const apiError = error instanceof ApiError ? error : undefined;

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await register(email, password, displayName);
      void navigate(from, { replace: true });
    } catch (failure) {
      setError(failure);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <CenteredLayout>
      <Card>
        <h2 className="mb-4 text-base font-semibold">Create an account</h2>

        {/* Field-level problems are rendered next to their field; anything
            else (a duplicate address, for example) goes to the banner. */}
        {apiError?.fieldErrors ? null : <ErrorBanner error={error} />}

        <form className="space-y-4" onSubmit={(event) => void submit(event)}>
          <Field label="Your name" error={apiError?.fieldError('displayName')}>
            <Input
              autoComplete="name"
              required
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
            />
          </Field>

          <Field label="Email" error={apiError?.fieldError('email')}>
            <Input
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
          </Field>

          <Field
            label="Password"
            hint="At least 12 characters. A passphrase beats a short, complicated one."
            error={apiError?.fieldError('password')}
          >
            <Input
              type="password"
              autoComplete="new-password"
              required
              minLength={12}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </Field>

          <Button type="submit" className="w-full" disabled={submitting}>
            {submitting ? 'Creating account…' : 'Create account'}
          </Button>
        </form>

        <p className="mt-4 text-sm text-[var(--color-muted)]">
          Already have an account?{' '}
          <Link to="/login" state={location.state} className="text-[var(--color-accent)] hover:underline">
            Sign in
          </Link>
        </p>
      </Card>
    </CenteredLayout>
  );
}
