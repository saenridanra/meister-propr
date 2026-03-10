import { vi, beforeEach, afterEach } from 'vitest'
import { config } from '@vue/test-utils'

// Mock global fetch
global.fetch = vi.fn()

// Mock sessionStorage
const sessionStorageStore: Record<string, string> = {}
const sessionStorageMock = {
  getItem: vi.fn((key: string) => sessionStorageStore[key] ?? null),
  setItem: vi.fn((key: string, value: string) => { sessionStorageStore[key] = value }),
  removeItem: vi.fn((key: string) => { delete sessionStorageStore[key] }),
  clear: vi.fn(() => { Object.keys(sessionStorageStore).forEach(k => delete sessionStorageStore[k]) }),
  length: 0,
  key: vi.fn(),
}
Object.defineProperty(global, 'sessionStorage', { value: sessionStorageMock, writable: true })

// Reset mocks between tests
beforeEach(() => {
  vi.clearAllMocks()
  sessionStorageMock.clear()
})

// Vue Test Utils global config
config.global.mocks = {
  $route: { params: {}, query: {}, name: 'clients' },
  $router: { push: vi.fn(), replace: vi.fn(), back: vi.fn() },
}

// Stub router components so tests don't need a real router plugin
config.global.stubs = {
  RouterLink: { template: '<a><slot /></a>' },
  RouterView: { template: '<div />' },
}
