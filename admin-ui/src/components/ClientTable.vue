<template>
  <div class="client-table-wrapper">
    <table v-if="filteredClients.length > 0" class="client-table">
      <thead>
        <tr>
          <th>Display Name</th>
          <th>Status</th>
          <th>ADO Credentials</th>
          <th>Created</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="client in filteredClients" :key="client.id">
          <td><RouterLink :to="'/' + client.id">{{ client.displayName }}</RouterLink></td>
          <td>
            <span :class="client.isActive ? 'badge-active' : 'badge-inactive'">
              {{ client.isActive ? 'Active' : 'Inactive' }}
            </span>
          </td>
          <td>
            <span :class="client.hasAdoCredentials ? 'badge-configured' : 'badge-none'">
              {{ client.hasAdoCredentials ? 'Configured' : 'None' }}
            </span>
          </td>
          <td>{{ formatDate(client.createdAt) }}</td>
        </tr>
      </tbody>
    </table>
    <p v-else class="empty-state">No clients found.</p>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

interface Client {
  id: string
  displayName: string
  isActive: boolean
  hasAdoCredentials: boolean
  createdAt: string
}

const props = defineProps<{
  clients: Client[]
  filter: string
}>()

const filteredClients = computed(() =>
  props.clients.filter((c) =>
    c.displayName.toLowerCase().includes(props.filter.toLowerCase())
  )
)

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString()
}
</script>
