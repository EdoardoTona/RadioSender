<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { Position, VueFlow } from '@vue-flow/core'
import { api } from '../api'
import type { RuntimeSnapshot } from '../types'
interface RadioNode {
  id: string
  name: string | null
  latencyMs: number | null
  signalStength: number | null
}
interface RadioHop {
  id: string
  from: string
  to: string
  latencyMs: number | null
  signalStength: number | null
}
const props = defineProps<{ nodeId: string; runtime: RuntimeSnapshot; disabled: boolean }>()
const emit = defineEmits<{ sent: [] }>()
const open = ref(false),
  error = ref(''),
  message = ref(''),
  busy = ref(false)
const network = ref<{ nodes: RadioNode[]; hops: RadioHop[] }>({ nodes: [], hops: [] })
let disposed = false,
  loading = false,
  generation = 0
const nodes = computed(() =>
  network.value.nodes.map((n, i) => ({
    id: n.id,
    position: { x: (i % 4) * 200, y: Math.floor(i / 4) * 100 },
    label: n.name ?? n.id,
    sourcePosition: Position.Right,
    targetPosition: Position.Left,
    style: { borderColor: '#808080', borderRadius: '2px', fontFamily: 'inherit', fontSize: '12px' },
  })),
)
const edges = computed(() =>
  network.value.hops
    .filter(
      (h) =>
        network.value.nodes.some((n) => n.id === h.from) &&
        network.value.nodes.some((n) => n.id === h.to),
    )
    .map((h) => ({
      id: h.id,
      source: h.from,
      target: h.to,
      label: h.latencyMs == null ? undefined : `${h.latencyMs} ms`,
      style: { stroke: '#808080' },
    })),
)
async function load() {
  if (loading || disposed || props.disabled || !open.value) return
  loading = true
  const current = generation
  try {
    const data = await api<typeof network.value>(
      `/nodes/${props.nodeId}/view?sessionId=${props.runtime.sessionId}&revision=${props.runtime.revision}`,
    )
    if (!disposed && current === generation) {
      network.value = data
      error.value = ''
    }
  } catch (e) {
    if (!disposed && current === generation) error.value = (e as Error).message
  } finally {
    loading = false
  }
}
async function ping() {
  busy.value = true
  try {
    const result = await api<{ message: string }>(`/nodes/${props.nodeId}/commands/ping`, 'POST', {
      sessionId: props.runtime.sessionId,
      revision: props.runtime.revision,
      arguments: {},
    })
    message.value = result.message
    error.value = ''
    emit('sent')
    await load()
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
watch(
  [() => props.nodeId, () => props.runtime.sessionId],
  () => {
    generation++
    network.value = { nodes: [], hops: [] }
    void load()
  },
)
watch(open, (value) => value && void load())
const timer = window.setInterval(load, 1000)
onBeforeUnmount(() => {
  disposed = true
  window.clearInterval(timer)
})
</script>
<template>
  <div class="module-commands">
    <v-btn block :disabled="disabled || busy" @click="ping">Ping radios</v-btn>
    <v-btn block :disabled="disabled" @click="open = true"
      >Radio network ({{ network.nodes.length }})</v-btn
    >
    <p class="hint">
      Nodes and links observed by this gateway. Updates continue while this panel is open.
    </p>
    <v-alert v-if="error" type="error">{{ error }}</v-alert>
    <p v-if="message" class="hint">{{ message }}</p>
  </div>
  <v-dialog v-model="open" max-width="1000"
    ><v-card>
      <v-card-title>Radio network</v-card-title>
      <v-card-text>
        <div v-if="network.nodes.length" style="height: 320px; border: 1px solid #d6d6d6">
          <VueFlow
            :id="`radio-${nodeId}`"
            :nodes="nodes"
            :edges="edges"
            :nodes-connectable="false"
            :nodes-draggable="false"
            :max-zoom="1"
            :delete-key-code="null"
            fit-view-on-init
          />
        </div>
        <p v-else class="hint">
          No radio nodes observed yet. Request a ping or wait for gateway traffic.
        </p>
        <v-table
          ><thead>
            <tr>
              <th>Node</th>
              <th>Address</th>
              <th>Latency</th>
              <th>Signal</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="n in network.nodes" :key="n.id">
              <td>{{ n.name ?? '—' }}</td>
              <td>{{ n.id }}</td>
              <td>{{ n.latencyMs == null ? '—' : `${n.latencyMs} ms` }}</td>
              <td>{{ n.signalStength ?? '—' }}</td>
            </tr>
          </tbody>
        </v-table>
      </v-card-text>
      <v-card-actions
        ><v-btn :disabled="disabled || busy" @click="ping">Ping radios</v-btn
        ><v-btn @click="open = false">Close</v-btn></v-card-actions
      >
    </v-card></v-dialog
  >
</template>
