import createClient from 'openapi-fetch'
import type { paths } from './generated/openapi'
import { useSession } from '@/composables/useSession'

export class UnauthorizedError extends Error {
  constructor() {
    super('Unauthorized')
    this.name = 'UnauthorizedError'
  }
}

export function createAdminClient(opts?: { overrideKey?: string }) {
  const { getAdminKey, clearAdminKey } = useSession()

  const client = createClient<paths>({
    baseUrl: import.meta.env.VITE_API_BASE_URL ?? '',
  })

  // Request middleware: inject X-Admin-Key
  client.use({
    async onRequest({ request }) {
      const key = opts?.overrideKey ?? getAdminKey()
      if (key) {
        request.headers.set('X-Admin-Key', key)
      }
      return request
    },
    async onResponse({ response }) {
      if (response.status === 401) {
        clearAdminKey()
        throw new UnauthorizedError()
      }
      return response
    },
  })

  return client
}
