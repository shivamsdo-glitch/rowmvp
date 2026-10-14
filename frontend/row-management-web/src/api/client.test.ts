import { describe, expect, it, vi } from 'vitest'
import { ApiError, createApiClient } from './client'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('api client', () => {
  it('sends the bearer token and query string, and parses JSON', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(json({ ok: true }))
    const api = createApiClient({ getToken: () => 'tok', fetchImpl })

    const result = await api.get('/api/v1/x', { a: 1, skip: undefined, b: 'two words' })

    expect(result).toEqual({ ok: true })
    const [url, init] = fetchImpl.mock.calls[0]
    expect(url).toBe('/api/v1/x?a=1&b=two+words')
    expect(init.headers.Authorization).toBe('Bearer tok')
  })

  it('omits Authorization when signed out', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(json({}))
    await createApiClient({ getToken: () => null, fetchImpl }).get('/api/v1/x')

    expect(fetchImpl.mock.calls[0][1].headers.Authorization).toBeUndefined()
  })

  it('posts JSON bodies with a content type', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(json({ id: 1 }, 201))
    await createApiClient({ getToken: () => null, fetchImpl }).post('/api/v1/x', { name: 'n' })

    const init = fetchImpl.mock.calls[0][1]
    expect(init.method).toBe('POST')
    expect(init.headers['Content-Type']).toBe('application/json')
    expect(init.body).toBe('{"name":"n"}')
  })

  it('sends multipart uploads without forcing a content type', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(json({ id: 1 }, 201))
    const form = new FormData()
    form.append('entityType', 'Parcel')
    await createApiClient({ getToken: () => null, fetchImpl }).upload('/api/v1/documents', form)

    const init = fetchImpl.mock.calls[0][1]
    expect(init.body).toBe(form)
    expect(init.headers['Content-Type']).toBeUndefined()
  })

  it('returns undefined for 204', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))

    await expect(createApiClient({ getToken: () => 't', fetchImpl }).delete('/api/v1/x/1')).resolves.toBeUndefined()
  })

  it('turns ProblemDetails into an ApiError with the stable code and field errors', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(
      json({ title: 'validation_failed', status: 400, detail: 'Bad input', traceId: 'abc', errors: { Name: ['Required'] } }, 400),
    )
    const err = (await createApiClient({ getToken: () => null, fetchImpl }).post('/api/v1/x', {}).catch((e: unknown) => e)) as ApiError

    expect(err).toBeInstanceOf(ApiError)
    expect(err).toMatchObject({ status: 400, code: 'validation_failed', traceId: 'abc', message: 'Bad input' })
    expect(err.fieldErrors.Name).toEqual(['Required'])
  })

  it('still throws a useful error when the body is not JSON', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response('<html>bad gateway</html>', { status: 502 }))
    const err = (await createApiClient({ getToken: () => null, fetchImpl }).get('/api/v1/x').catch((e: unknown) => e)) as ApiError

    expect(err).toBeInstanceOf(ApiError)
    expect(err.status).toBe(502)
    expect(err.code).toBe('unknown_error')
  })

  it('reports an expired session on 401 only when a token was sent', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(json({ title: 'unauthorized' }, 401))
    const onUnauthorized = vi.fn()

    await createApiClient({ getToken: () => 'tok', fetchImpl, onUnauthorized }).get('/x').catch(() => {})
    expect(onUnauthorized).toHaveBeenCalledTimes(1)

    await createApiClient({ getToken: () => null, fetchImpl, onUnauthorized }).get('/x').catch(() => {})
    expect(onUnauthorized).toHaveBeenCalledTimes(1)
  })
})
