import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { UnauthorizedError } from '@/services/api'

// Mock the api module
vi.mock('@/services/api', () => ({
  UnauthorizedError: class UnauthorizedError extends Error {
    constructor() {
      super('Unauthorized')
      this.name = 'UnauthorizedError'
    }
  },
  createAdminClient: vi.fn(),
}))

// Mock the router
const mockRouterPush = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockRouterPush }),
}))

// Mock useSession
const mockSetAdminKey = vi.fn()
const mockIsAuthenticated = { value: false }
vi.mock('@/composables/useSession', () => ({
  useSession: () => ({
    setAdminKey: mockSetAdminKey,
    isAuthenticated: mockIsAuthenticated,
  }),
}))

async function importLoginView() {
  const mod = await import('@/views/LoginView.vue')
  return mod.default
}

describe('LoginView', () => {
  let createAdminClient: ReturnType<typeof vi.fn>
  let mockGet: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    const api = await import('@/services/api')
    createAdminClient = vi.mocked(api.createAdminClient)
    mockGet = vi.fn()
    createAdminClient.mockReturnValue({ GET: mockGet })
  })

  it('renders admin key input and submit button', async () => {
    const LoginView = await importLoginView()
    const wrapper = mount(LoginView)
    expect(wrapper.find('input[type="password"]').exists()).toBe(true)
    expect(wrapper.find('button[type="submit"]').exists()).toBe(true)
  })

  it('shows validation error without API call when key is empty', async () => {
    const LoginView = await importLoginView()
    const wrapper = mount(LoginView)
    await wrapper.find('form').trigger('submit')
    expect(createAdminClient).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Admin key is required')
  })

  it('calls createAdminClient with overrideKey on submit', async () => {
    mockGet.mockResolvedValue({ data: [], response: { ok: true } })
    const LoginView = await importLoginView()
    const wrapper = mount(LoginView)
    await wrapper.find('input[type="password"]').setValue('my-key')
    await wrapper.find('form').trigger('submit')
    await wrapper.vm.$nextTick()
    expect(createAdminClient).toHaveBeenCalledWith({ overrideKey: 'my-key' })
    expect(mockGet).toHaveBeenCalledWith('/clients', {})
  })

  it('stores key and navigates to / on 200 success', async () => {
    mockGet.mockResolvedValue({ data: [], response: { ok: true } })
    const LoginView = await importLoginView()
    const wrapper = mount(LoginView)
    await wrapper.find('input[type="password"]').setValue('valid-key')
    await wrapper.find('form').trigger('submit')
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    expect(mockSetAdminKey).toHaveBeenCalledWith('valid-key')
    expect(mockRouterPush).toHaveBeenCalledWith('/')
  })

  it('shows error and does not store key on UnauthorizedError', async () => {
    const { UnauthorizedError: MockUnauthorizedError } = await import('@/services/api')
    mockGet.mockRejectedValue(new MockUnauthorizedError())
    const LoginView = await importLoginView()
    const wrapper = mount(LoginView)
    await wrapper.find('input[type="password"]').setValue('bad-key')
    await wrapper.find('form').trigger('submit')
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    expect(mockSetAdminKey).not.toHaveBeenCalled()
    expect(mockRouterPush).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Invalid admin key')
  })
})
