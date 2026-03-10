import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'

const mockGet = vi.fn()
const mockPatch = vi.fn()
vi.mock('@/services/api', () => ({
  createAdminClient: vi.fn(() => ({ GET: mockGet, PATCH: mockPatch })),
  UnauthorizedError: class UnauthorizedError extends Error {},
}))

const mockRouterPush = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockRouterPush }),
  useRoute: () => ({ params: { id: 'client-1' } }),
  RouterLink: { template: '<a><slot /></a>' },
}))

vi.mock('@/components/AdoCredentialsForm.vue', () => ({
  default: {
    name: 'AdoCredentialsForm',
    props: ['clientId', 'hasCredentials'],
    template: '<div class="ado-form-stub" />',
  },
}))

vi.mock('@/components/ConfirmDialog.vue', () => ({
  default: {
    name: 'ConfirmDialog',
    props: ['open', 'message'],
    emits: ['confirm', 'cancel'],
    template: '<div class="confirm-dialog-stub" />',
  },
}))

const sampleClient = {
  id: 'client-1',
  displayName: 'Acme Corp',
  isActive: true,
  hasAdoCredentials: false,
  createdAt: '2024-01-01T00:00:00Z',
}

describe('ClientDetailView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches client on mount and renders displayName in an editable input', async () => {
    mockGet.mockResolvedValue({ data: sampleClient })
    const { default: ClientDetailView } = await import('@/views/ClientDetailView.vue')
    const wrapper = mount(ClientDetailView)
    await flushPromises()
    const input = wrapper.find('input[name="displayName"]')
    expect(input.exists()).toBe(true)
    expect((input.element as HTMLInputElement).value).toBe('Acme Corp')
  })

  it('calls PATCH with updated displayName on Save', async () => {
    mockGet.mockResolvedValue({ data: sampleClient })
    mockPatch.mockResolvedValue({ data: { ...sampleClient, displayName: 'New Name' }, response: { ok: true } })
    const { default: ClientDetailView } = await import('@/views/ClientDetailView.vue')
    const wrapper = mount(ClientDetailView)
    await flushPromises()
    await wrapper.find('input[name="displayName"]').setValue('New Name')
    await wrapper.find('button.save-btn').trigger('click')
    await flushPromises()
    expect(mockPatch).toHaveBeenCalledWith(
      '/clients/{clientId}',
      expect.objectContaining({ params: { path: { clientId: 'client-1' } }, body: { displayName: 'New Name' } })
    )
  })

  it('calls PATCH with toggled isActive on Disable button', async () => {
    mockGet.mockResolvedValue({ data: sampleClient })
    mockPatch.mockResolvedValue({ data: { ...sampleClient, isActive: false }, response: { ok: true } })
    const { default: ClientDetailView } = await import('@/views/ClientDetailView.vue')
    const wrapper = mount(ClientDetailView)
    await flushPromises()
    const toggleBtn = wrapper.find('button.toggle-status-btn')
    expect(toggleBtn.text()).toBe('Disable')
    await toggleBtn.trigger('click')
    await flushPromises()
    expect(mockPatch).toHaveBeenCalledWith(
      '/clients/{clientId}',
      expect.objectContaining({ params: { path: { clientId: 'client-1' } }, body: { isActive: false } })
    )
  })

  it('shows not-found message and navigates home on 404', async () => {
    mockGet.mockResolvedValue({ data: null, response: { status: 404, ok: false } })
    const { default: ClientDetailView } = await import('@/views/ClientDetailView.vue')
    const wrapper = mount(ClientDetailView)
    await flushPromises()
    expect(wrapper.text()).toContain('Client not found')
    expect(mockRouterPush).toHaveBeenCalledWith('/')
  })
})
