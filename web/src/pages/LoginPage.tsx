import { useState } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthProvider';
import { CenteredLayout } from '../components/Layout';
import { Button, Card, ErrorBanner, Field, Input } from '../components/ui';

export function LoginPage() {
  const { status, login } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  const from = (location.state as { from?: string } | null)?.from ?? '/orgs';

  if (status === 'authenticated') {
    return <Navigate to={from} replace />;
  }

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await login(email, password);
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
        <h2 className="mb-4 text-base font-semibold">Sign in</h2>
        <ErrorBanner error={error} />

        <form className="space-y-4" onSubmit={(event) => void submit(event)}>
          <Field label="Email">
            <Input
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
          </Field>

          <Field label="Password">
            <Input
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </Field>

          <Button type="submit" className="w-full" disabled={submitting}>
            {submitting ? 'Signing in…' : 'Sign in'}
          </Button>
        </form>

        <p className="mt-4 text-sm text-[var(--color-muted)]">
          No account?{' '}
          <Link
            to="/register"
            state={location.state}
            className="text-[var(--color-accent)] hover:underline"
          >
            Create one
          </Link>
        </p>
      </Card>
    </CenteredLayout>
  );
}
