/** Thin typed fetch wrapper. Hand-rolled on purpose; see ADR 0005. */

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  traceId?: string
  errors?: Record<string, string[]>
}

/** A non-2xx response. `code` is the API's stable machine code (ProblemDetails `title`). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId?: string
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail || problem.title || `Request failed (${status})`)
    this.name = 'ApiError'
    this.status = status
    this.code = problem.title ?? 'unknown_error'
    this.traceId = problem.traceId
    this.fieldErrors = problem.errors ?? {}
  }
}

export interface ApiClientOptions {
  baseUrl?: string
  getToken: () => string | null
  /** Called when a request made with a token comes back 401 (expired or revoked). */
  onUnauthorized?: () => void
  fetchImpl?: typeof fetch
}

type Query = Record<string, string | number | boolean | undefined | null>

export interface ApiClient {
  get<T>(path: string, query?: Query): Promise<T>
  post<T>(path: string, body?: unknown): Promise<T>
  put<T>(path: string, body?: unknown): Promise<T>
  delete(path: string): Promise<void>
  upload<T>(path: string, form: FormData): Promise<T>
}

export function createApiClient(options: ApiClientOptions): ApiClient {
  const base = (options.baseUrl ?? '').replace(/\/$/, '')

  function url(path: string, query?: Query) {
    const params = new URLSearchParams()
    for (const [k, v] of Object.entries(query ?? {})) if (v !== undefined && v !== null) params.set(k, String(v))
    const qs = params.toString()
    return `${base}${path}${qs ? `?${qs}` : ''}`
  }

  async function send<T>(method: string, path: string, init: { query?: Query; body?: unknown; form?: FormData }): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' }
    const token = options.getToken()
    if (token) headers.Authorization = `Bearer ${token}`

    let body: BodyInit | undefined
    if (init.form) body = init.form // the browser sets the multipart boundary
    else if (init.body !== undefined) {
      headers['Content-Type'] = 'application/json'
      body = JSON.stringify(init.body)
    }

    const response = await (options.fetchImpl ?? fetch)(url(path, init.query), { method, headers, body })

    if (!response.ok) {
      if (response.status === 401 && token) options.onUnauthorized?.()
      let problem: ProblemDetails = {}
      try {
        problem = await response.json()
      } catch {
        // non-JSON error body (proxy page, empty 500): fall back to the status
      }
      throw new ApiError(response.status, problem)
    }

    if (response.status === 204) return undefined as T
    return (await response.json()) as T
  }

  return {
    get: (path, query) => send('GET', path, { query }),
    post: (path, body) => send('POST', path, { body }),
    put: (path, body) => send('PUT', path, { body }),
    delete: (path) => send<void>('DELETE', path, {}),
    upload: (path, form) => send('POST', path, { form }),
  }
}
