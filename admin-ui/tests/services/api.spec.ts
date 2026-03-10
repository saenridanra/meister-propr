import { describe, it, expect, vi, beforeEach } from 'vitest'

// We test the middleware behaviour by inspecting what fetch receives
const SESSION_KEY = 'meisterpropr_admin_key'

function mockFetch(status: number, body: unknown = {}) {
  const response = new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
  vi.mocked(global.fetch).mockResolvedValueOnce(response)
}

describe('createAdminClient', () => {
  let createAdminClient: typeof import('@/services/api').createAdminClient
  // Must be re-imported after vi.resetModules() so instanceof uses the same class
  let UnauthorizedError: typeof import('@/services/api').UnauthorizedError
  let clearAdminKey: () => void

  beforeEach(async () => {
    vi.resetModules()
    const api = await import('@/services/api')
    const session = await import('@/composables/useSession')
    createAdminClient = api.createAdminClient
    UnauthorizedError = api.UnauthorizedError
    clearAdminKey = session.useSession().clearAdminKey
  })

  it('injects X-Admin-Key from sessionStorage in requests', async () => {
    sessionStorage.setItem(SESSION_KEY, 'stored-key')
    mockFetch(200, [])
    const client = createAdminClient()
    await client.GET('/clients', {})
    // openapi-fetch calls fetch(request) — headers are on the Request object (first arg)
    const [requestArg] = vi.mocked(global.fetch).mock.calls[0]
    const headers = (requestArg as Request).headers
    expect(headers.get('x-admin-key')).toBe('stored-key')
  })

  it('overrideKey takes precedence over sessionStorage key', async () => {
    sessionStorage.setItem(SESSION_KEY, 'stored-key')
    mockFetch(200, [])
    const client = createAdminClient({ overrideKey: 'candidate-key' })
    await client.GET('/clients', {})
    const [requestArg] = vi.mocked(global.fetch).mock.calls[0]
    const headers = (requestArg as Request).headers
    expect(headers.get('x-admin-key')).toBe('candidate-key')
  })

  it('overrideKey works when sessionStorage is empty', async () => {
    mockFetch(200, [])
    const client = createAdminClient({ overrideKey: 'candidate-key' })
    await client.GET('/clients', {})
    const [requestArg] = vi.mocked(global.fetch).mock.calls[0]
    const headers = (requestArg as Request).headers
    expect(headers.get('x-admin-key')).toBe('candidate-key')
  })

  it('throws UnauthorizedError and clears session on 401', async () => {
    sessionStorage.setItem(SESSION_KEY, 'stored-key')
    mockFetch(401)
    const client = createAdminClient()
    await expect(client.GET('/clients', {})).rejects.toBeInstanceOf(UnauthorizedError)
    expect(sessionStorage.removeItem).toHaveBeenCalledWith(SESSION_KEY)
  })
})
