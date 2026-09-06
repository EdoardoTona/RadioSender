<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import type { Connection } from '@vue-flow/core'
import { api, chooseFile, finishDesktopClose, isDesktop } from './api'
import {
  copy,
  emptyDocument,
  emptyFilter,
  id,
  type DocumentSnapshot,
  type FlowDocument,
  type FlowIssue,
  type ModuleDescriptor,
  type RuntimeSnapshot,
} from './types'
import FlowCanvas from './components/FlowCanvas.vue'
import SettingsForm from './components/SettingsForm.vue'
import FilterEditor from './components/FilterEditor.vue'
import ManualInput from './components/ManualInput.vue'
import NodeInspector from './components/NodeInspector.vue'

const modules = ref<ModuleDescriptor[]>([]),
  document = ref<FlowDocument>(emptyDocument()),
  snapshot = ref<DocumentSnapshot | null>(null)
const runtime = ref<RuntimeSnapshot | null>(null),
  online = ref(false),
  tick = ref(0)
const selectedNodeId = ref<string | null>(null),
  selectedEdgeId = ref<string | null>(null)
const editVersion = ref(0),
  savedVersion = ref(0),
  saving = ref(false),
  saveError = ref(''),
  error = ref(''),
  notice = ref(''),
  busy = ref(false)
const issues = ref<FlowIssue[]>([]),
  applyDialog = ref(false),
  librarySearch = ref(''),
  showConnections = ref(false)
const connectionFrom = ref(''),
  connectionTo = ref(''),
  defaultDirectory = ref(''),
  pathDialog = ref(false),
  pathValue = ref(''),
  pathMode = ref<'new' | 'open' | 'save-as'>('new')
let pendingPathResolve: ((path: string | null) => void) | null = null
let adopting = false,
  saveTimer: ReturnType<typeof setTimeout> | undefined,
  savingPromise: Promise<void> | null = null,
  hub: HubConnection | null = null
let polling: ReturnType<typeof setInterval> | undefined
const node = computed(() => document.value.nodes.find((n) => n.id === selectedNodeId.value))
const edge = computed(() => document.value.edges.find((e) => e.id === selectedEdgeId.value))
const definition = computed(() => modules.value.find((m) => m.type === node.value?.type))
const sameDocument = computed(
  () => !!snapshot.value && runtime.value?.documentId === snapshot.value.id,
)
const unapplied = computed(
  () =>
    !sameDocument.value ||
    runtime.value?.revision !== snapshot.value?.revision ||
    editVersion.value !== savedVersion.value,
)
const saveLabel = computed(() =>
  saveError.value
    ? 'Save failed'
    : saving.value
      ? 'Saving…'
      : editVersion.value !== savedVersion.value
        ? 'Unsaved changes'
        : snapshot.value
          ? 'Saved'
          : 'No document',
)
const nodeState = computed(() =>
  sameDocument.value ? runtime.value?.nodes.find((n) => n.id === node.value?.id) : undefined,
)
const fileName = computed(
  () => snapshot.value?.path.split(/[\\/]/).pop() ?? 'No configuration open',
)
const visibleModules = computed(() =>
  modules.value.filter((m) =>
    (m.name + m.description).toLowerCase().includes(librarySearch.value.toLowerCase()),
  ),
)
const outputNodes = computed(() =>
  document.value.nodes.filter((n) => modules.value.find((m) => m.type === n.type)?.outputs.length),
)
const inputNodes = computed(() =>
  document.value.nodes.filter((n) => modules.value.find((m) => m.type === n.type)?.inputs.length),
)
const inspectorNode = computed(() => {
  if (sameDocument.value && node.value && definition.value)
    return { id: node.value.id, name: node.value.name, category: definition.value.category }
  return null
})

watch(
  document,
  () => {
    if (adopting || !snapshot.value) return
    editVersion.value++
    issues.value = []
    clearTimeout(saveTimer)
    saveTimer = setTimeout(() => {
      void flushSave().catch(() => {})
    }, 650)
  },
  { deep: true, flush: 'sync' },
)

function adopt(value: DocumentSnapshot) {
  adopting = true
  snapshot.value = value
  document.value = copy(value.document)
  editVersion.value = 0
  savedVersion.value = 0
  selectedNodeId.value = null
  selectedEdgeId.value = null
  saveError.value = ''
  issues.value = []
  error.value = ''
  adopting = false
  localStorage.setItem('radiosender.lastDocument', value.path)
}
async function flushSave() {
  clearTimeout(saveTimer)
  if (savingPromise) {
    await savingPromise
    if (editVersion.value !== savedVersion.value) await flushSave()
    return
  }
  if (!snapshot.value || (editVersion.value === savedVersion.value && !saveError.value)) return
  saving.value = true
  savingPromise = (async () => {
    while (snapshot.value && (editVersion.value !== savedVersion.value || saveError.value)) {
      const version = editVersion.value,
        current = snapshot.value
      try {
        const updated = await api<DocumentSnapshot>(`/documents/${current.id}`, 'PUT', {
          revision: current.revision,
          document: copy(document.value),
        })
        snapshot.value = updated
        savedVersion.value = version
        saveError.value = ''
      } catch (e) {
        saveError.value = (e as Error).message
        throw e
      }
    }
  })()
  try {
    await savingPromise
  } finally {
    savingPromise = null
    saving.value = false
  }
}
async function saveNow() {
  try {
    await flushSave()
    if (snapshot.value)
      await api(`/documents/${snapshot.value.id}/save`, 'POST', {
        revision: snapshot.value.revision,
      })
    notice.value = 'Configuration saved.'
  } catch (e) {
    saveError.value = (e as Error).message
  }
}
async function selectPath(mode: 'new' | 'open' | 'save-as'): Promise<string | null> {
  pathMode.value = mode
  const initial =
    mode === 'open'
      ? (snapshot.value?.path ?? defaultDirectory.value)
      : `${defaultDirectory.value}/new-flow.radiosender.json`
  if (isDesktop) return chooseFile(mode === 'open' ? 'open' : 'save', initial)
  pathValue.value = initial
  pathDialog.value = true
  return new Promise((resolve) => {
    pendingPathResolve = resolve
  })
}
function finishPath(path: string | null) {
  pathDialog.value = false
  pendingPathResolve?.(path)
  pendingPathResolve = null
}
async function fileAction(mode: 'new' | 'open' | 'save-as') {
  if (busy.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    if (mode !== 'save-as') await flushSave()
    else {
      clearTimeout(saveTimer)
      if (savingPromise) await savingPromise.catch(() => {})
    }
    const path = await selectPath(mode)
    if (!path) return
    if (mode === 'save-as' && snapshot.value) {
      const saved = await api<DocumentSnapshot>(`/documents/${snapshot.value.id}/save-as`, 'POST', {
        revision: snapshot.value.revision,
        document: copy(document.value),
        path,
      })
      snapshot.value = saved
      savedVersion.value = editVersion.value
      saveError.value = ''
      localStorage.setItem('radiosender.lastDocument', path)
    } else
      adopt(
        await api<DocumentSnapshot>(`/documents/${mode === 'new' ? 'new' : 'open'}`, 'POST', {
          path,
        }),
      )
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
async function reload() {
  if (
    !snapshot.value ||
    !window.confirm('Reload the file from disk? Unsaved edits in this editor will be discarded.')
  )
    return
  clearTimeout(saveTimer)
  if (savingPromise) await savingPromise.catch(() => {})
  try {
    adopt(await api<DocumentSnapshot>('/documents/open', 'POST', { path: snapshot.value.path }))
  } catch (e) {
    error.value = (e as Error).message
  }
}
function addNode(module: ModuleDescriptor) {
  if (!snapshot.value) return
  const nodeId = id(),
    count = document.value.nodes.length
  document.value.nodes.push({
    id: nodeId,
    type: module.type,
    name: module.name,
    enabled: true,
    settings: copy(module.defaults),
  })
  document.value.editor.positions[nodeId] = {
    x: 60 + (count % 3) * 290,
    y: 70 + Math.floor(count / 3) * 180,
  }
  selectNode(nodeId)
}
function selectNode(nodeId: string) {
  selectedNodeId.value = nodeId
  selectedEdgeId.value = null
}
function selectEdge(edgeId: string) {
  selectedEdgeId.value = edgeId
  selectedNodeId.value = null
}
function connect(connection: Connection) {
  if (!connection.source || !connection.target) return
  if (connection.source === connection.target) {
    error.value = 'A node cannot connect to itself.'
    return
  }
  if (
    document.value.edges.some(
      (e) => e.from.node === connection.source && e.to.node === connection.target,
    )
  ) {
    error.value = 'These nodes are already connected.'
    return
  }
  const edgeId = id()
  document.value.edges.push({
    id: edgeId,
    from: { node: connection.source, port: connection.sourceHandle ?? 'out' },
    to: { node: connection.target, port: connection.targetHandle ?? 'in' },
    enabled: true,
  })
  selectEdge(edgeId)
}
function removeSelection() {
  if (node.value) {
    const nodeId = node.value.id
    document.value.nodes = document.value.nodes.filter((n) => n.id !== nodeId)
    document.value.edges = document.value.edges.filter(
      (e) => e.from.node !== nodeId && e.to.node !== nodeId,
    )
    delete document.value.editor.positions[nodeId]
    selectedNodeId.value = null
  } else if (edge.value) {
    document.value.edges = document.value.edges.filter((e) => e.id !== edge.value!.id)
    selectedEdgeId.value = null
  }
}
async function prepareApply() {
  error.value = ''
  notice.value = ''
  busy.value = true
  try {
    await flushSave()
    issues.value = await api<FlowIssue[]>('/validate', 'POST', document.value)
    if (!issues.value.length) applyDialog.value = true
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
async function apply() {
  if (!snapshot.value) return
  busy.value = true
  error.value = ''
  try {
    await flushSave()
    const result = await api<{ revision: number; restarted: string[]; kept: string[] }>(
      '/runtime/apply',
      'POST',
      { documentId: snapshot.value.id, revision: snapshot.value.revision },
    )
    await refreshRuntime()
    applyDialog.value = false
    notice.value = `Revision ${result.revision} applied. ${result.restarted.length} node(s) started, ${result.kept.length} kept running.`
  } catch (e) {
    error.value = (e as Error).message
    applyDialog.value = false
    await refreshRuntime()
  } finally {
    busy.value = false
  }
}
async function stop() {
  busy.value = true
  error.value = ''
  try {
    await api('/runtime/stop', 'POST', {})
    await refreshRuntime()
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
async function refreshRuntime() {
  try {
    runtime.value = await api<RuntimeSnapshot>('/runtime')
    online.value = true
    tick.value++
  } catch {
    online.value = false
  }
}
function exportDocument() {
  const blob = new Blob([JSON.stringify(document.value, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob),
    link = window.document.createElement('a')
  link.href = url
  link.download = fileName.value
  link.click()
  URL.revokeObjectURL(url)
}
function beforeUnload(event: BeforeUnloadEvent) {
  if (saving.value || editVersion.value !== savedVersion.value || saveError.value) {
    event.preventDefault()
    event.returnValue = ''
  }
}
async function desktopClosing() {
  try {
    await flushSave()
    finishDesktopClose()
  } catch {
    error.value = 'The window remains open because saving failed. Retry Save or choose Save As.'
  }
}
onMounted(async () => {
  window.addEventListener('beforeunload', beforeUnload)
  window.addEventListener('desktop-closing', desktopClosing)
  try {
    const [catalog, defaults] = await Promise.all([
      api<ModuleDescriptor[]>('/modules'),
      api<{ directory: string }>('/defaults'),
    ])
    modules.value = catalog
    defaultDirectory.value = defaults.directory
    const last = localStorage.getItem('radiosender.lastDocument')
    if (last) {
      try {
        adopt(await api<DocumentSnapshot>('/documents/open', 'POST', { path: last }))
      } catch {
        notice.value = 'The previous file could not be reopened. Choose Open or New.'
      }
    }
    await refreshRuntime()
    hub = new HubConnectionBuilder()
      .withUrl('/flowHub')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Error)
      .build()
    hub.on('Changed', (state: RuntimeSnapshot) => {
      runtime.value = state
      online.value = true
      tick.value++
    })
    hub.onreconnected(refreshRuntime)
    hub.onclose(() => {
      online.value = false
    })
    await hub.start().catch(() => {})
    polling = setInterval(() => {
      if (hub?.state !== 'Connected') {
        void refreshRuntime()
        if (hub?.state === 'Disconnected') void hub.start().catch(() => {})
      }
    }, 2000)
  } catch (e) {
    error.value = (e as Error).message
  }
})
onBeforeUnmount(() => {
  clearTimeout(saveTimer)
  clearInterval(polling)
  void hub?.stop()
  window.removeEventListener('beforeunload', beforeUnload)
  window.removeEventListener('desktop-closing', desktopClosing)
})
</script>

<template>
  <div class="app-shell">
    <header class="app-header">
      <a class="brand" href="/flows/index.html"><span class="brand-icon">↗</span>RadioSender</a>
      <nav>
        <span class="current">Flows</span><a href="/Log">Log</a><a href="/Stats">Statistics</a>
      </nav>
      <span class="connection-state"
        ><span class="state-dot" :class="{ on: online }"></span
        >{{ online ? 'Connected' : 'Reconnecting…' }}</span
      >
    </header>
    <div class="document-toolbar">
      <div class="document-title">
        <strong>{{ fileName }}</strong
        ><small :title="snapshot?.path">{{
          snapshot?.path ?? 'Create or open a configuration to get started'
        }}</small>
      </div>
      <span class="save-state" :class="{ failed: saveError }">{{ saveLabel }}</span>
      <div class="toolbar-actions">
        <button :disabled="busy" @click="fileAction('new')">New</button
        ><button :disabled="busy" @click="fileAction('open')">Open</button
        ><button :disabled="!snapshot || busy" @click="saveNow">Save</button
        ><button :disabled="!snapshot || busy" @click="fileAction('save-as')">Save As</button
        ><button :disabled="!snapshot" title="Download a JSON copy" @click="exportDocument">
          Export</button
        ><span class="toolbar-divider"></span
        ><button v-if="runtime?.running" :disabled="busy" @click="stop">Stop</button
        ><button class="primary" :disabled="!snapshot || busy || !online" @click="prepareApply">
          {{ busy ? 'Working…' : runtime?.running ? 'Apply changes' : 'Apply & start' }}
        </button>
      </div>
    </div>
    <div class="runtime-strip">
      <span class="state-dot" :class="{ on: runtime?.running }"></span
      ><span v-if="runtime?.running"
        >Running: <strong>{{ runtime.path?.split(/[\\/]/).pop() }}</strong> · revision
        {{ runtime.revision }}</span
      ><span v-else>Flow stopped</span
      ><span v-if="snapshot && unapplied" class="tag amber">Unapplied changes</span
      ><span class="runtime-note">Autosave keeps your work. Apply updates the running flow.</span>
    </div>
    <div v-if="error || saveError || runtime?.error" class="alert error" role="alert">
      <span>{{ error || saveError || runtime?.error }}</span
      ><button v-if="saveError" @click="saveNow">Retry save</button
      ><button v-if="saveError" @click="reload">Reload file</button
      ><button
        @click="
          error = '';
          notice = ''
        "
      >
        ×
      </button>
    </div>
    <div v-if="notice" class="alert info" role="status">
      <span>{{ notice }}</span
      ><button @click="notice = ''">×</button>
    </div>
    <div v-if="issues.length" class="validation-list" role="alert">
      <strong>Resolve these issues before applying:</strong
      ><button
        v-for="issue in issues"
        :key="issue.elementId + issue.field"
        @click="
          document.nodes.some((n) => n.id === issue.elementId)
            ? selectNode(issue.elementId)
            : selectEdge(issue.elementId)
        "
      >
        {{ issue.message }}
      </button>
    </div>
    <main class="workspace">
      <aside class="library">
        <div class="panel-heading">
          <span class="eyebrow">Build a flow</span>
          <h2>Module library</h2>
        </div>
        <input
          v-model="librarySearch"
          class="library-search"
          aria-label="Search modules"
          placeholder="Search modules…"
        />
        <template v-for="category in ['Source', 'Processor', 'Target']" :key="category"
          ><h3 class="category-heading">
            {{ category === 'Processor' ? 'Processing' : category + 's' }}
          </h3>
          <button
            v-for="module in visibleModules.filter((m) => m.category === category)"
            :key="module.type"
            class="library-module"
            :disabled="!snapshot"
            @click="addNode(module)"
          >
            <span class="module-symbol">{{
              category === 'Source' ? '↗' : category === 'Target' ? '↙' : '→'
            }}</span
            ><span
              ><strong>{{ module.name }}</strong
              ><small>{{ module.description }}</small></span
            ><span class="add-symbol">+</span>
          </button></template
        >
        <div class="library-foot">
          <strong>One source, many destinations</strong>
          <p>Connect an output to multiple inputs. Add filters directly to each connection.</p>
        </div>
      </aside>
      <div class="flow-workspace">
        <div class="canvas-toolbar">
          <span>{{ document.nodes.length }} nodes · {{ document.edges.length }} connections</span
          ><button :class="{ active: showConnections }" @click="showConnections = !showConnections">
            {{ showConnections ? 'Hide connection list' : 'Connection list' }}
          </button>
        </div>
        <FlowCanvas
          :key="snapshot?.id ?? 'empty'"
          :document="document"
          :modules="modules"
          :runtime="runtime"
          :same-document="sameDocument"
          :selected-node="selectedNodeId"
          :selected-edge="selectedEdgeId"
          @node="selectNode"
          @edge="selectEdge"
          @connect="connect"
          @move="(nodeId, x, y) => (document.editor.positions[nodeId] = { x, y })"
          @viewport="
            (value) => {
              if (snapshot) document.editor.viewport = value
            }
          "
        />
        <div v-if="showConnections" class="connection-list">
          <form
            @submit.prevent="
              connect({
                source: connectionFrom,
                target: connectionTo,
                sourceHandle: 'out',
                targetHandle: 'in',
              })
            "
          >
            <select v-model="connectionFrom" aria-label="Connection source" required>
              <option value="">Output node…</option>
              <option v-for="n in outputNodes" :key="n.id" :value="n.id">
                {{ n.name }}
              </option></select
            ><span>→</span
            ><select v-model="connectionTo" aria-label="Connection target" required>
              <option value="">Input node…</option>
              <option v-for="n in inputNodes" :key="n.id" :value="n.id">
                {{ n.name }}
              </option></select
            ><button type="submit" :disabled="!snapshot">Connect</button>
          </form>
          <button
            v-for="e in document.edges"
            :key="e.id"
            class="connection-row"
            @click="selectEdge(e.id)"
          >
            {{ document.nodes.find((n) => n.id === e.from.node)?.name }} →
            {{ document.nodes.find((n) => n.id === e.to.node)?.name
            }}<span>{{ e.filter?.enabled ? 'Filter & mapping' : 'Pass through' }}</span>
          </button>
        </div>
        <NodeInspector
          v-if="inspectorNode"
          :key="inspectorNode.id"
          :node-id="inspectorNode.id"
          :name="inspectorNode.name"
          :category="inspectorNode.category"
          :runtime="runtime"
          :tick="tick"
        />
        <div v-else class="inspector-placeholder">
          {{
            !sameDocument && runtime?.running
              ? 'Open the running document to inspect its events.'
              : 'Select a running node to inspect input, output and delivery.'
          }}
        </div>
      </div>
      <aside class="properties">
        <template v-if="node"
          ><div class="panel-heading">
            <span class="eyebrow">Node settings</span>
            <h2>{{ definition?.name ?? 'Unavailable module' }}</h2>
          </div>
          <div class="properties-content">
            <label class="field"
              ><span>Name</span><input v-model="node.name" maxlength="100" /></label
            ><label class="field check"
              ><span>Enabled</span><input v-model="node.enabled" type="checkbox"
            /></label>
            <div v-if="nodeState" class="status-card">
              <strong>{{ nodeState.status }}</strong
              ><small>{{ nodeState.detail }}</small
              ><small v-if="nodeState.pending">{{ nodeState.pending }} pending delivery</small>
            </div>
            <SettingsForm
              v-if="definition"
              :node="node"
              :definition="definition"
              @update="(key, value) => (node!.settings[key] = value)"
            /><ManualInput
              v-if="definition?.view === 'manual-input' && runtime"
              :key="node.id"
              :node-id="node.id"
              :runtime="runtime"
              :disabled="!sameDocument || !nodeState || !runtime.running"
              @sent="refreshRuntime"
            />
            <p v-if="definition?.category === 'Processor'" class="hint">
              Events pass through unchanged. Use the input and output inspector to examine this
              point in the flow.
            </p>
            <button class="danger subtle wide" @click="removeSelection">Remove node</button>
          </div></template
        >
        <template v-else-if="edge"
          ><div class="panel-heading">
            <span class="eyebrow">Connection settings</span>
            <h2>Filter & mapping</h2>
          </div>
          <div class="properties-content">
            <p class="connection-description">
              {{ document.nodes.find((n) => n.id === edge!.from.node)?.name }}<br />↓<br />{{
                document.nodes.find((n) => n.id === edge!.to.node)?.name
              }}
            </p>
            <label class="field check"
              ><span>Connection enabled</span><input v-model="edge.enabled" type="checkbox"
            /></label>
            <div class="field-pair">
              <button @click="selectNode(edge!.from.node)">Inspect before</button
              ><button @click="selectNode(edge!.to.node)">Inspect after</button>
            </div>
            <p v-if="sameDocument" class="hint">
              {{ runtime?.edges.find((e) => e.id === edge!.id)?.forwarded ?? 0 }} forwarded ·
              {{ runtime?.edges.find((e) => e.id === edge!.id)?.filtered ?? 0 }} filtered
            </p>
            <button v-if="!edge.filter" class="wide" @click="edge.filter = emptyFilter()">
              Add filter & mapping</button
            ><FilterEditor v-else :key="edge.id" v-model="edge.filter" /><button
              v-if="edge.filter"
              class="subtle wide"
              @click="edge.filter = null"
            >
              Remove filter</button
            ><button class="danger subtle wide" @click="removeSelection">Remove connection</button>
          </div></template
        >
        <div v-else class="properties-empty">
          <span class="empty-symbol">⌁</span>
          <h2>Make a connection</h2>
          <p>Select a node to configure it, send events and inspect its stream.</p>
          <p>Select a connection to filter or map the events it carries.</p>
        </div>
      </aside>
    </main>
    <div v-if="pathDialog" class="modal-backdrop">
      <form
        class="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="path-title"
        @submit.prevent="finishPath(pathValue)"
      >
        <span class="eyebrow">Configuration document</span>
        <h2 id="path-title">
          {{
            pathMode === 'open'
              ? 'Open configuration'
              : pathMode === 'new'
                ? 'New configuration'
                : 'Save configuration as'
          }}
        </h2>
        <p class="hint">
          Choose a file on this RadioSender computer. New files start autosaving immediately.
        </p>
        <label class="field"
          ><span>Full JSON file path</span><input v-model="pathValue" required autofocus /></label
        ><small>Choose an unused name when creating or saving a copy.</small>
        <div class="modal-actions">
          <button type="button" @click="finishPath(null)">Cancel</button
          ><button type="submit" class="primary">
            {{ pathMode === 'open' ? 'Open file' : 'Save file' }}
          </button>
        </div>
      </form>
    </div>
    <div v-if="applyDialog" class="modal-backdrop">
      <div class="modal" role="dialog" aria-modal="true" aria-labelledby="apply-title">
        <span class="eyebrow">Runtime update</span>
        <h2 id="apply-title">Apply this configuration?</h2>
        <p>
          {{ document.nodes.filter((n) => n.enabled).length }} enabled nodes and
          {{ document.edges.filter((e) => e.enabled).length }} enabled connections.
        </p>
        <p class="hint">
          Changed connection settings restart the affected node. Unchanged connections keep running.
          Events already accepted are drained before switching.
        </p>
        <p v-if="runtime?.running && !sameDocument" class="hint">
          This replaces the currently running document and starts a new inspection session.
        </p>
        <div class="modal-actions">
          <button :disabled="busy" @click="applyDialog = false">Cancel</button
          ><button class="primary" :disabled="busy" @click="apply">
            {{ busy ? 'Applying…' : 'Apply configuration' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
