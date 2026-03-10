<template>
  <div id="app">
    <AppHeader v-if="isAuthenticated" />
    <RouterView />
    <AppNotification />
  </div>
</template>

<script setup lang="ts">
import { RouterView } from 'vue-router'
import AppHeader from '@/components/AppHeader.vue'
import AppNotification from '@/components/AppNotification.vue'
import { useSession } from '@/composables/useSession'

const { isAuthenticated } = useSession()
</script>
