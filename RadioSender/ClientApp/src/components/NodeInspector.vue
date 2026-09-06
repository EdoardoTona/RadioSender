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
watch(() => [props.nodeId, props.runtime?.sessionId, direction.value], reset)
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
      <div>
        <span class="eyebrow">Stream inspector</span><strong>{{ name }}</strong>
      </div>
      <div class="segmented">
        <button
          v-if="category !== 'Source'"
          :class="{ active: direction === 'input' }"
          @click="direction = 'input'"
        >
          Input</button
        ><button
          v-if="category !== 'Target'"
          :class="{ active: direction === 'output' }"
          @click="direction = 'output'"
        >
          Output</button
        ><button
          v-if="category === 'Target'"
          :class="{ active: direction === 'delivery' }"
          @click="direction = 'delivery'"
        >
          Delivery
        </button>
      </div>
      <input
        v-model="query"
        aria-label="Search events"
        placeholder="Find identifier, control, status…"
        class="event-search"
      />
      <button @click="paused = !paused">{{ paused ? 'Resume view' : 'Pause view' }}</button
      ><button
        v-if="canReplay"
        :disabled="!selected.length || !active || busy"
        @click="prepareReplay"
      >
        {{ label }} ({{ selected.length }})
      </button>
    </div>
    <p v-if="expired" class="inspector-note">
      Some older events have expired from the bounded history.
    </p>
    <p v-if="error" role="alert" class="error-text inspector-note">{{ error }}</p>
    <p v-if="message" class="success-text inspector-note">{{ message }}</p>
    <div class="event-table-wrap">
      <table class="event-table">
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
          <tr v-for="item in visible" :key="item.id" :class="{ replayed: item.replayOf }">
            <td v-if="canReplay">
              <input
                v-model="selected"
                type="checkbox"
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
              {{ item.status }}<span v-if="item.replayOf" class="tag">Replay</span
              ><small v-if="item.detail">{{ item.detail }}</small>
            </td>
          </tr>
          <tr v-if="!visible.length">
            <td :colspan="canReplay ? 11 : 10" class="empty-events">
              {{ paused ? 'View paused. Data continues to flow.' : 'No events at this point yet.' }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="replayConfirm" class="modal-backdrop">
      <div class="modal" role="dialog" aria-modal="true" aria-labelledby="replay-title">
        <h2 id="replay-title">{{ label }}</h2>
        <p>Send {{ selected.length }} selected event(s) to {{ targets }}?</p>
        <p class="hint">
          {{
            category === 'Target'
              ? 'Uses the stored target input without repeating the incoming edge filter.'
              : 'Uses the stored output and current downstream filters. Upstream mappings are not repeated.'
          }}
          Running revision: {{ replayContext?.revision }}.
        </p>
        <p v-if="error" class="error-text">{{ error }}</p>
        <div class="modal-actions">
          <button :disabled="busy" @click="replayConfirm = false">Cancel</button
          ><button class="primary" :disabled="busy" @click="replay">
            {{ busy ? 'Sending…' : 'Send selected events' }}
          </button>
        </div>
      </div>
    </div>
  </section>
</template>
