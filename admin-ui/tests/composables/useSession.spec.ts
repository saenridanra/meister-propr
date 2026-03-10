import { describe, it, expect, beforeEach } from 'vitest'
import { useSession } from '@/composables/useSession'

const SESSION_KEY = 'meisterpropr_admin_key'

describe('useSession', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  it('setAdminKey stores the key in sessionStorage', () => {
    const { setAdminKey } = useSession()
    setAdminKey('my-secret')
    expect(sessionStorage.setItem).toHaveBeenCalledWith(SESSION_KEY, 'my-secret')
  })

  it('getAdminKey returns the stored value', () => {
    const { setAdminKey, getAdminKey } = useSession()
    setAdminKey('my-secret')
    expect(getAdminKey()).toBe('my-secret')
  })

  it('clearAdminKey removes the key from sessionStorage', () => {
    const { setAdminKey, clearAdminKey, getAdminKey } = useSession()
    setAdminKey('my-secret')
    clearAdminKey()
    expect(sessionStorage.removeItem).toHaveBeenCalledWith(SESSION_KEY)
    expect(getAdminKey()).toBeNull()
  })

  it('isAuthenticated is false when sessionStorage is empty', () => {
    const { isAuthenticated } = useSession()
    expect(isAuthenticated.value).toBe(false)
  })

  it('isAuthenticated is true after setAdminKey', () => {
    const { setAdminKey, isAuthenticated } = useSession()
    setAdminKey('my-secret')
    expect(isAuthenticated.value).toBe(true)
  })

  it('isAuthenticated is false after clearAdminKey', () => {
    const { setAdminKey, clearAdminKey, isAuthenticated } = useSession()
    setAdminKey('my-secret')
    clearAdminKey()
    expect(isAuthenticated.value).toBe(false)
  })
})
