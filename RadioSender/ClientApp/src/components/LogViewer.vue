<script setup lang="ts">
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { api } from '../api'

interface LogEntry {
  id: number
  log: {
    timestamp: string
    level: string
    message: string
    exception: string | null
    nodeId: string | null
    edgeId: string | null
  }
}
const props = defineProps<{ nodeId?: string; sessionId?: string; tick: number }>()
const includeNodes = ref(false),
  paused = ref(false),
  query = ref(''),
  level = ref('All levels')
const items = ref<LogEntry[]>([]),
  cursor = ref(0),
  error = ref(''),
  expired = ref(false)
let generation = 0,
  loading = false,
  disposed = false
const rows = computed(() =>
  items.value
    .filter(
      (e) =>
        (level.value === 'All levels' || e.log.level === level.value) &&
        (!query.value ||
          (e.log.message + ' ' + e.log.nodeId).toLowerCase().includes(query.value.toLowerCase())),
    )
    .slice()
    .reverse(),
)
async function load() {
  if (loading || paused.value || disposed) return
  loading = true
  const current = generation
  try {
    const params = new URLSearchParams({
      scope: props.nodeId ? 'node' : includeNodes.value ? 'all' : 'general',
      after: String(cursor.value),
    })
    if (props.nodeId) params.set('nodeId', props.nodeId)
    if (props.sessionId) params.set('sessionId', props.sessionId)
    const page = await api<{ cursor: number; historyExpired: boolean; items: LogEntry[] }>(
      '/logs?' + params,
    )
    if (current !== generation || disposed) return
    const known = new Set(items.value.map((e) => e.id))
    items.value.push(...page.items.filter((e) => !known.has(e.id)))
    if (items.value.length > 500) items.value.splice(0, items.value.length - 500)
    cursor.value = page.cursor
    expired.value ||= page.historyExpired
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading = false
  }
}
watch([() => props.nodeId, () => props.sessionId, includeNodes], () => {
  generation++
  cursor.value = 0
  items.value = []
  expired.value = false
  void load()
})
watch(() => props.tick, load, { immediate: true })
watch(paused, (value) => {
  if (!value) void load()
})
onBeforeUnmount(() => {
  disposed = true
})
</script>

<template>
  <section class="log-viewer">
    <div class="log-toolbar">
      <v-text-field v-model="query" label="Search logs" class="log-search" clearable />
      <v-select
        v-model="level"
        :items="['All levels', 'Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal']"
        label="Level"
        class="level-select"
      />
      <v-switch v-if="!nodeId" v-model="includeNodes" label="Include node logs" />
      <v-btn @click="paused = !paused">{{ paused ? 'Resume view' : 'Pause view' }}</v-btn>
    </div>
    <v-alert v-if="error" type="error">{{ error }}</v-alert>
    <p v-if="expired" class="inspector-note">
      Some older entries have expired from the bounded history.
    </p>
    <v-table fixed-header class="log-table"
      ><thead>
        <tr>
          <th>Time</th>
          <th>Level</th>
          <th v-if="!nodeId && includeNodes">Node</th>
          <th>Message</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="entry in rows" :key="entry.id">
          <td>{{ new Date(entry.log.timestamp).toLocaleTimeString() }}</td>
          <td>
            <v-chip
              :color="
                ['Error', 'Fatal'].includes(entry.log.level)
                  ? 'error'
                  : entry.log.level === 'Warning'
                    ? 'warning'
                    : 'secondary'
              "
              size="x-small"
              >{{ entry.log.level }}</v-chip
            >
          </td>
          <td v-if="!nodeId && includeNodes">{{ entry.log.nodeId ?? 'General' }}</td>
          <td class="log-message">
            {{ entry.log.message }}
            <details v-if="entry.log.exception">
              <summary>Exception details</summary>
              <pre>{{ entry.log.exception }}</pre>
            </details>
          </td>
        </tr>
        <tr v-if="!rows.length">
          <td :colspan="includeNodes ? 4 : 3" class="empty-events">No matching log entries.</td>
        </tr>
      </tbody>
    </v-table>
  </section>
</template>
