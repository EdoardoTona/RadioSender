<script setup lang="ts">
import { computed } from 'vue'
import {
  Handle,
  Position,
  VueFlow,
  useVueFlow,
  type Connection,
  type NodeDragEvent,
} from '@vue-flow/core'
import type { FlowDocument, ModuleDescriptor, RuntimeSnapshot } from '../types'

const props = defineProps<{
  document: FlowDocument
  modules: ModuleDescriptor[]
  runtime: RuntimeSnapshot | null
  sameDocument: boolean
  selectedNode: string | null
  selectedEdge: string | null
  readOnly?: boolean
}>()
const emit = defineEmits<{
  node: [id: string]
  edge: [id: string]
  connect: [connection: Connection]
  move: [id: string, x: number, y: number]
  viewport: [value: { x: number; y: number; zoom: number }]
}>()
const { fitView, zoomIn, zoomOut } = useVueFlow()
const nodes = computed(() =>
  props.document.nodes.map((node, index) => {
    const definition = props.modules.find((m) => m.type === node.type)
    const state = props.sameDocument ? props.runtime?.nodes.find((n) => n.id === node.id) : null
    return {
      id: node.id,
      type: 'module',
      position: props.document.editor.positions[node.id] ?? { x: 80 + index * 260, y: 120 },
      selected: props.selectedNode === node.id,
      data: { label: node.name, definition, state, enabled: node.enabled, type: node.type },
    }
  }),
)
const edges = computed(() =>
  props.document.edges.map((edge) => ({
    id: edge.id,
    source: edge.from.node,
    sourceHandle: edge.from.port,
    target: edge.to.node,
    targetHandle: edge.to.port,
    selected: props.selectedEdge === edge.id,
    label: !edge.enabled
      ? 'Disabled'
      : [
          props.document.filters?.find((f) => f.id === edge.filterId)?.name,
          edge.delayMs ? `${edge.delayMs} ms` : null,
        ]
          .filter(Boolean)
          .join(' · ') || undefined,
    style: {
      stroke: !edge.enabled ? '#b8b8b8' : props.selectedEdge === edge.id ? '#0067c0' : '#808080',
      strokeWidth: props.selectedEdge === edge.id ? 3 : 2,
      strokeDasharray: !edge.enabled ? '5 5' : undefined,
    },
    labelBgPadding: [7, 5] as [number, number],
    labelBgBorderRadius: 2,
  })),
)
function moved(event: NodeDragEvent) {
  emit('move', event.node.id, event.node.position.x, event.node.position.y)
}
</script>

<template>
  <div class="canvas">
    <VueFlow
      :nodes="nodes"
      :edges="edges"
      :default-viewport="document.editor.viewport ?? { x: 0, y: 0, zoom: 1 }"
      :min-zoom="0.2"
      :max-zoom="2"
      :delete-key-code="null"
      :nodes-draggable="!readOnly"
      :nodes-connectable="!readOnly"
      :snap-to-grid="true"
      :snap-grid="[10, 10]"
      @node-click="emit('node', $event.node.id)"
      @edge-click="emit('edge', $event.edge.id)"
      @connect="emit('connect', $event)"
      @node-drag-stop="moved"
      @move-end="emit('viewport', $event.flowTransform)"
    >
      <template #node-module="{ data }">
        <div
          class="module-node"
          :class="[{ disabled: !data.enabled }, data.definition?.category.toLowerCase()]"
        >
          <Handle
            v-for="port in data.definition?.inputs ?? []"
            :id="port"
            :key="port"
            type="target"
            :position="Position.Left"
          />
          <div class="node-category">{{ data.definition?.category ?? 'Unavailable' }}</div>
          <strong>{{ data.label }}</strong>
          <div class="node-footer">
            <span
              class="state-dot"
              :class="
                data.state?.status === 'Running' ||
                data.state?.status === 'Connected' ||
                data.state?.status === 'Listening'
                  ? 'on'
                  : ''
              "
            ></span
            >{{ !data.enabled ? 'Disabled' : (data.state?.status ?? 'Not running')
            }}<span class="node-kind">{{ data.definition?.name ?? data.type }}</span>
          </div>
          <Handle
            v-for="port in data.definition?.outputs ?? []"
            :id="port"
            :key="port"
            type="source"
            :position="Position.Right"
          />
        </div>
      </template>
    </VueFlow>
    <div v-if="!document.nodes.length" class="canvas-empty">
      <h2>No nodes</h2>
      <p>Add a source from the library, then connect its output to a target.</p>
    </div>
    <div class="canvas-controls">
      <v-btn title="Zoom out" @click="zoomOut()">−</v-btn>
      <v-btn title="Fit graph" @click="fitView({ padding: 0.25 })">Fit</v-btn>
      <v-btn title="Zoom in" @click="zoomIn()">+</v-btn>
    </div>
    <div v-if="!readOnly" class="canvas-hint">
      Drag between ports to connect · Select a connection to add a filter
    </div>
  </div>
</template>
