/**
 * The single place the frontend talks to the API.
 *
 * Token handling, deliberately:
 * - The access token lives in memory only. Nothing is written to
 *   localStorage or sessionStorage, so a cross-site scripting bug has no
 *   stored credential to steal.
 * - The refresh token is an HttpOnly cookie the browser holds and this code
 *   cannot read. Sessions survive a page reload because the app asks the API
 *   to refresh on startup.
 * - A 401 triggers exactly one refresh attempt, shared between concurrent
 *   requests, then the original request is retried once.
 */

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly fieldErrors: Record<string, string[]> | undefined;

  constructor(
    status: number,
    message: string,
    code?: string,
    fieldErrors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }

  /** First error for a field, if the server reported one. */
  fieldError(field: string): string | undefined {
    const camel = field.charAt(0).toLowerCase() + field.slice(1);
    return this.fieldErrors?.[camel]?.[0] ?? this.fieldErrors?.[field]?.[0];
  }
}

let accessToken: string | null = null;
let onSessionLost: (() => void) | null = null;
let refreshInFlight: Promise<boolean> | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

/** Called when a refresh fails, so the app can return to the sign-in screen. */
export function setSessionLostHandler(handler: (() => void) | null): void {
  onSessionLost = handler;
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Skip the refresh-and-retry dance (used by the auth calls themselves). */
  anonymous?: boolean;
  signal?: AbortSignal;
}

async function parseError(response: Response): Promise<ApiError> {
  let message = `Request failed with status ${response.status}`;
  let code: string | undefined;
  let fieldErrors: Record<string, string[]> | undefined;

  try {
    const problem = (await response.json()) as {
      detail?: string;
      title?: string;
      code?: string;
      errors?: Record<string, string[]>;
    };

    message = problem.detail ?? problem.title ?? message;
    code = problem.code;
    fieldErrors = problem.errors;
  } catch {
    // Non-JSON error body (a proxy error page, for example): keep the default.
  }

  return new ApiError(response.status, message, code, fieldErrors);
}

async function refreshSession(): Promise<boolean> {
  refreshInFlight ??= (async () => {
    try {
      const response = await fetch('/api/auth/refresh', {
        method: 'POST',
        credentials: 'include',
      });

      if (!response.ok) {
        return false;
      }

      const result = (await response.json()) as { accessToken: string };
      accessToken = result.accessToken;
      return true;
    } catch {
      return false;
    } finally {
      // Allow the next 401 to try again.
      setTimeout(() => {
        refreshInFlight = null;
      }, 0);
    }
  })();

  return refreshInFlight;
}

async function send(path: string, options: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json' };

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  if (accessToken && !options.anonymous) {
    headers.Authorization = `Bearer ${accessToken}`;
  }

  return fetch(`/api${path}`, {
    method: options.method ?? 'GET',
    headers,
    credentials: 'include',
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    ...(options.signal ? { signal: options.signal } : {}),
  });
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let response = await send(path, options);

  if (response.status === 401 && !options.anonymous) {
    const refreshed = await refreshSession();

    if (refreshed) {
      response = await send(path, options);
    } else {
      onSessionLost?.();
      throw await parseError(response);
    }
  }

  if (!response.ok) {
    throw await parseError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  return (text.length === 0 ? undefined : JSON.parse(text)) as T;
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) =>
    request<T>(path, signal ? { signal } : {}),
  post: <T>(path: string, body?: unknown) => request<T>(path, { method: 'POST', body }),
  patch: <T>(path: string, body?: unknown) => request<T>(path, { method: 'PATCH', body }),
  put: <T>(path: string, body?: unknown) => request<T>(path, { method: 'PUT', body }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),

  /** Auth calls bypass refresh-and-retry: a 401 from them is the answer. */
  anonymous: {
    post: <T>(path: string, body?: unknown) =>
      request<T>(path, { method: 'POST', body, anonymous: true }),
    get: <T>(path: string) => request<T>(path, { anonymous: true }),
  },

  refreshSession,
};
