<template>
  <div class="client-detail-view">
    <RouterLink to="/" class="back-link">← Back to clients</RouterLink>
    <p v-if="notFound" class="error">Client not found.</p>
    <p v-else-if="loading" class="loading">Loading…</p>
    <template v-else-if="client">
      <h2>{{ client.displayName }}</h2>

      <section class="edit-section">
        <label for="displayName">Display Name</label>
        <input
          id="displayName"
          name="displayName"
          v-model="editedDisplayName"
          type="text"
        />
        <button class="save-btn" @click="saveDisplayName" :disabled="saving">Save</button>
        <span v-if="saveError" class="error">{{ saveError }}</span>
      </section>

      <section class="status-section">
        <span>Status: {{ client.isActive ? 'Active' : 'Inactive' }}</span>
        <button class="toggle-status-btn" @click="toggleStatus" :disabled="saving">
          {{ client.isActive ? 'Disable' : 'Enable' }}
        </button>
      </section>

      <section class="ado-section">
        <AdoCredentialsForm
          :clientId="client.id"
          :hasCredentials="client.hasAdoCredentials"
          @credentials-updated="client.hasAdoCredentials = true"
          @credentials-cleared="client.hasAdoCredentials = false"
        />
      </section>

      <section class="danger-zone">
        <button class="delete-btn btn-danger" @click="showDeleteDialog = true">Delete Client</button>
        <ConfirmDialog
          :open="showDeleteDialog"
          message="Delete this client permanently?"
          @confirm="handleDelete"
          @cancel="showDeleteDialog = false"
        />
      </section>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter, useRoute, RouterLink } from 'vue-router'
import AdoCredentialsForm from '@/components/AdoCredentialsForm.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { createAdminClient } from '@/services/api'

interface Client {
  id: string
  displayName: string
  isActive: boolean
  hasAdoCredentials: boolean
  createdAt: string
}

const router = useRouter()
const route = useRoute()
const clientId = route.params.id as string

const client = ref<Client | null>(null)
const loading = ref(false)
const notFound = ref(false)
const saving = ref(false)
const saveError = ref('')
const showDeleteDialog = ref(false)
const editedDisplayName = ref('')

onMounted(async () => {
  loading.value = true
  try {
    const { data, response } = await createAdminClient().GET('/clients/{clientId}', {
      params: { path: { clientId } },
    })
    if (response && (response as Response).status === 404) {
      notFound.value = true
      router.push('/')
      return
    }
    client.value = data as Client
    editedDisplayName.value = (data as Client).displayName
  } catch {
    notFound.value = true
    router.push('/')
  } finally {
    loading.value = false
  }
})

async function saveDisplayName() {
  if (!client.value) return
  saving.value = true
  saveError.value = ''
  try {
    const { data } = await createAdminClient().PATCH('/clients/{clientId}', {
      params: { path: { clientId } },
      body: { displayName: editedDisplayName.value },
    })
    client.value = data as Client
  } catch {
    saveError.value = 'Failed to save.'
  } finally {
    saving.value = false
  }
}

async function toggleStatus() {
  if (!client.value) return
  saving.value = true
  try {
    const { data } = await createAdminClient().PATCH('/clients/{clientId}', {
      params: { path: { clientId } },
      body: { isActive: !client.value.isActive },
    })
    client.value = data as Client
  } catch {
    saveError.value = 'Failed to update status.'
  } finally {
    saving.value = false
  }
}

async function handleDelete() {
  try {
    await createAdminClient().DELETE('/clients/{clientId}', {
      params: { path: { clientId } },
    })
    router.push('/')
  } catch {
    router.push('/')
  }
}
</script>
