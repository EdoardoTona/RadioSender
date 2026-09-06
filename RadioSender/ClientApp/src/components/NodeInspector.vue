<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { api } from '../api'
import type { Observation, ObservationPage, RuntimeSnapshot } from '../types'
const props = defineProps<{
  nodeId: string
  name: string
  category: string
  runtime: RuntimeSnapshot | null
  tick: number
}>()
const items = ref<Observation[]>([]),
  selected = ref<number[]>([]),
  cursor = ref(0),
  expired = ref(false)
const direction = ref(props.category === 'Source' ? 'output' : 'input'),
  paused = ref(false),
  query = ref(''),
  error = ref(''),
  busy = ref(false)
const replayConfirm = ref(false),
  operationId = ref<string | null>(null),
  message = ref('')
const replayContext = ref<{ sessionId: string; revision: number; observationIds: number[] } | null>(
  null,
)
let generation = 0,
  loading = false,
  disposed = false
const visible = computed(() =>
  items.value
    .filter(
      (o) =>
        !query.value ||
        [o.punch.competitorId, o.punch.control, o.punch.competitorStatus, o.status].some((v) =>
          String(v).toLowerCase().includes(query.value.toLowerCase()),
        ),
    )
    .slice()
    .reverse(),
)
const canReplay = computed(
  () =>
    direction.value === 'output' || (direction.value === 'input' && props.category === 'Target'),
)
const active = computed(
  () => props.runtime?.running && props.runtime.nodes.some((n) => n.id === props.nodeId),
)
const label = computed(() => (props.category === 'Target' ? 'Retry target' : 'Replay output'))
const targets = computed(() => {
  if (props.category === 'Target') return props.name
  return 'all connected downstream targets in the running revision'
})
async function load() {
  if (loading || paused.value || disposed) return
  loading = true
  const current = generation
  try {
    const page = await api<ObservationPage>(
      `/nodes/${props.nodeId}/observations?direction=${direction.value}&after=${cursor.value}`,
    )
    if (current !== generation || disposed) return
    expired.value ||= page.historyExpired
    if (page.items.length)
      items.value = [...items.value, ...page.items]
        .filter((item, index, all) => all.findIndex((x) => x.id === item.id) === index)
        .slice(-500)
    cursor.value = page.cursor
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    loading = false
  }
}
function reset() {
  generation++
  cursor.value = 0
  items.value = []
  selected.value = []
  expired.value = false
  replayConfirm.value = false
  operationId.value = null
  void load()
}
watch([() => props.nodeId, () => props.runtime?.sessionId, direction], reset)
watch(() => props.tick, load, { immediate: true })
watch(paused, (value) => {
  if (!value) void load()
})
watch(selected, () => {
  operationId.value = null
  replayConfirm.value = false
})
onBeforeUnmount(() => {
  disposed = true
})
function prepareReplay() {
  if (!props.runtime) return
  replayContext.value = {
    sessionId: props.runtime.sessionId,
    revision: props.runtime.revision,
    observationIds: [...selected.value],
  }
  operationId.value = crypto.randomUUID()
  replayConfirm.value = true
  error.value = ''
  message.value = ''
}
async function replay() {
  if (!replayContext.value) return
  busy.value = true
  error.value = ''
  operationId.value ??= crypto.randomUUID()
  try {
    await api(
      `/nodes/${props.nodeId}/${props.category === 'Target' ? 'retry' : 'replay'}`,
      'POST',
      {
        ...replayContext.value,
        operationId: operationId.value,
        port: direction.value === 'output' ? 'out' : 'in',
      },
    )
    replayConfirm.value = false
    message.value = `${selected.value.length} event(s) accepted for replay.`
    selected.value = []
    await load()
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="inspector">
    <div class="inspector-toolbar">
      <strong>{{ name }}</strong>
      <div class="segmented">
        <v-btn
          v-if="category !== 'Source'"
          :variant="direction === 'input' ? 'tonal' : 'text'"
          @click="direction = 'input'"
          >Input</v-btn
        >
        <v-btn
          v-if="category !== 'Target'"
          :variant="direction === 'output' ? 'tonal' : 'text'"
          @click="direction = 'output'"
          >Output</v-btn
        >
        <v-btn
          v-if="category === 'Target'"
          :variant="direction === 'delivery' ? 'tonal' : 'text'"
          @click="direction = 'delivery'"
          >Delivery</v-btn
        >
      </div>
      <v-text-field
        v-model="query"
        label="Search events"
        placeholder="Identifier, control, status…"
        class="event-search"
        clearable
      />
      <v-btn @click="paused = !paused">{{ paused ? 'Resume view' : 'Pause view' }}</v-btn>
      <v-btn v-if="canReplay" :disabled="!selected.length || !active || busy" @click="prepareReplay"
        >{{ label }} ({{ selected.length }})</v-btn
      >
    </div>
    <p v-if="expired" class="inspector-note">
      Some older events have expired from the bounded history.
    </p>
    <p v-if="error" role="alert" class="error-text inspector-note">{{ error }}</p>
    <p v-if="message" class="success-text inspector-note">{{ message }}</p>
    <v-table class="event-table" fixed-header height="230" density="compact">
      <thead>
        <tr>
          <th v-if="canReplay">Select</th>
          <th>Observed</th>
          <th>Identifier</th>
          <th>ID type</th>
          <th>Control</th>
          <th>Type</th>
          <th>Event time</th>
          <th>Status</th>
          <th>Cancellation</th>
          <th>Revision</th>
          <th>Delivery / origin</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="item in visible"
          :key="item.id"
          :class="{ replayed: item.replayOf }"
          :data-event-id="item.id"
        >
          <td v-if="canReplay">
            <v-checkbox-btn
              v-model="selected"
              :value="item.id"
              :aria-label="`Select event ${item.id}`"
              :disabled="busy || (selected.length >= 250 && !selected.includes(item.id))"
            />
          </td>
          <td>{{ new Date(item.observedAt).toLocaleTimeString() }}</td>
          <td>
            <strong>{{ item.punch.competitorId }}</strong>
          </td>
          <td>{{ item.punch.competitorIdType }}</td>
          <td>{{ item.punch.control }}</td>
          <td>{{ item.punch.controlType }}</td>
          <td>{{ item.punch.time }}</td>
          <td>{{ item.punch.competitorStatus }}</td>
          <td>{{ item.punch.cancellation ? 'Yes' : '—' }}</td>
          <td>{{ item.revision }}</td>
          <td :title="item.detail ?? item.edgeId ?? ''">
            {{ item.status
            }}<v-chip v-if="item.replayOf" size="x-small" color="secondary">Replay</v-chip
            ><small v-if="item.detail">{{ item.detail }}</small>
          </td>
        </tr>
        <tr v-if="!visible.length">
          <td :colspan="canReplay ? 11 : 10" class="empty-events">
            {{ paused ? 'View paused. Data continues to flow.' : 'No events at this point yet.' }}
          </td>
        </tr>
      </tbody>
    </v-table>
    <v-dialog v-model="replayConfirm" max-width="520" :persistent="busy"
      ><v-card
        ><v-card-title>{{ label }}</v-card-title
        ><v-card-text
          ><p>
            Send {{ replayContext?.observationIds.length }} selected event(s) to {{ targets }}?
          </p>
          <p class="hint">
            {{
              category === 'Target'
                ? 'Uses the stored target input without repeating the incoming edge filter.'
                : 'Uses the stored output and current downstream filters. Upstream mappings are not repeated.'
            }}
            Running revision: {{ replayContext?.revision }}.
          </p>
          <p v-if="error" class="error-text">{{ error }}</p></v-card-text
        ><v-card-actions
          ><v-btn :disabled="busy" @click="replayConfirm = false">Cancel</v-btn
          ><v-btn color="primary" variant="flat" :loading="busy" @click="replay"
            >Send selected events</v-btn
          ></v-card-actions
        ></v-card
      ></v-dialog
    >
  </section>
</template>
