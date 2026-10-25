import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { api, setAccessToken, setSessionLostHandler } from '../api/client';
import type { AuthResult, MeResponse, OrganizationMembership, Permission, User } from '../api/types';

type SessionStatus = 'loading' | 'anonymous' | 'authenticated';

interface AuthContextValue {
  status: SessionStatus;
  user: User | null;
  organizations: OrganizationMembership[];
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  logout: () => Promise<void>;
  /** Re-reads /auth/me, after joining or leaving an organization. */
  reload: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * Holds the session.
 *
 * On startup it asks the API to refresh: the access token only ever lives in
 * memory, so a reload has nothing to restore from except the HttpOnly cookie.
 * That is the point — a token this code could read is a token an injected
 * script could read too.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<SessionStatus>('loading');
  const [user, setUser] = useState<User | null>(null);
  const [organizations, setOrganizations] = useState<OrganizationMembership[]>([]);

  const applyMe = useCallback((me: MeResponse) => {
    setUser(me.user);
    setOrganizations(me.organizations);
    setStatus('authenticated');
  }, []);

  const clear = useCallback(() => {
    setAccessToken(null);
    setUser(null);
    setOrganizations([]);
    setStatus('anonymous');
  }, []);

  const reload = useCallback(async () => {
    applyMe(await api.get<MeResponse>('/auth/me'));
  }, [applyMe]);

  useEffect(() => {
    setSessionLostHandler(clear);

    let cancelled = false;

    void (async () => {
      const refreshed = await api.refreshSession();

      if (cancelled) {
        return;
      }

      if (!refreshed) {
        setStatus('anonymous');
        return;
      }

      try {
        await reload();
      } catch {
        if (!cancelled) {
          clear();
        }
      }
    })();

    return () => {
      cancelled = true;
      setSessionLostHandler(null);
    };
  }, [clear, reload]);

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      organizations,

      login: async (email, password) => {
        const result = await api.anonymous.post<AuthResult>('/auth/login', { email, password });
        setAccessToken(result.accessToken);
        await reload();
      },

      register: async (email, password, displayName) => {
        const result = await api.anonymous.post<AuthResult>('/auth/register', {
          email,
          password,
          displayName,
        });
        setAccessToken(result.accessToken);
        await reload();
      },

      logout: async () => {
        try {
          await api.post('/auth/logout');
        } finally {
          clear();
        }
      },

      reload,
    }),
    [status, user, organizations, reload, clear],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);

  if (!context) {
    throw new Error('useAuth must be used inside AuthProvider.');
  }

  return context;
}

/**
 * The caller's membership in one organization, with a permission check.
 *
 * This decides what the UI offers, never what is allowed: the server
 * re-authorizes every request, so hiding a button is a courtesy and not a
 * security control.
 */
export function useMembership(organizationId: string | undefined) {
  const { organizations } = useAuth();

  return useMemo(() => {
    const membership = organizations.find((candidate) => candidate.organizationId === organizationId);

    return {
      membership,
      role: membership?.role,
      can: (permission: Permission) => membership?.permissions.includes(permission) ?? false,
    };
  }, [organizations, organizationId]);
}
