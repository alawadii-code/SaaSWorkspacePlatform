import { useCallback, useEffect, useState, type DependencyList } from 'react';

interface LoaderState<T> {
  data: T | null;
  error: unknown;
  loading: boolean;
  reload: () => void;
  setError: (error: unknown) => void;
}

/**
 * Minimal data-loading hook: fetch on mount and when dependencies change,
 * expose loading and error state, and allow an explicit reload after a
 * mutation.
 *
 * A data library would add caching and deduplication, which this app does not
 * need — every screen reads one or two endpoints — so the dependency is not
 * worth it here.
 */
export function useLoader<T>(load: () => Promise<T>, deps: DependencyList): LoaderState<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(true);
  const [reloadToken, setReloadToken] = useState(0);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const run = useCallback(load, deps);

  useEffect(() => {
    let cancelled = false;

    setLoading(true);

    run()
      .then((result) => {
        if (!cancelled) {
          setData(result);
          setError(null);
        }
      })
      .catch((failure: unknown) => {
        if (!cancelled) {
          setError(failure);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [run, reloadToken]);

  const reload = useCallback(() => setReloadToken((token) => token + 1), []);

  return { data, error, loading, reload, setError };
}
