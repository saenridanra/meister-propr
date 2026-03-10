<template>
  <header class="app-header">
    <span class="app-title">Meister ProPR Admin</span>
    <button class="logout-btn" @click="logout">Logout</button>
  </header>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useSession } from '@/composables/useSession'

const router = useRouter()
const { clearAdminKey } = useSession()

function logout() {
  clearAdminKey()
  router.push('/login')
}
</script>
