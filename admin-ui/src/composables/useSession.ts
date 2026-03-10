import { computed } from 'vue'

const SESSION_KEY = 'meisterpropr_admin_key'

export function useSession() {
  function setAdminKey(key: string): void {
    sessionStorage.setItem(SESSION_KEY, key)
  }

  function getAdminKey(): string | null {
    return sessionStorage.getItem(SESSION_KEY)
  }

  function clearAdminKey(): void {
    sessionStorage.removeItem(SESSION_KEY)
  }

  const isAuthenticated = computed(() => getAdminKey() !== null)

  return { setAdminKey, getAdminKey, clearAdminKey, isAuthenticated }
}
