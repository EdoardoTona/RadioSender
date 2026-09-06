<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import type { Connection } from '@vue-flow/core'
import {
  mdiPlus,
  mdiFolderOpenOutline,
  mdiPencilOutline,
  mdiArrowLeft,
  mdiPlay,
  mdiStop,
  mdiContentSaveOutline,
} from '@mdi/js'
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
  type RuntimeGraph,
  type RuntimeSnapshot,
} from './types'
import FlowCanvas from './components/FlowCanvas.vue'
import SettingsForm from './components/SettingsForm.vue'
import FilterEditor from './components/FilterEditor.vue'
import ModuleControls from './components/ModuleControls.vue'
import NodeInspector from './components/NodeInspector.vue'
import LogViewer from './components/LogViewer.vue'

const mode = ref<'flow' | 'editor' | 'logs'>('flow')
const modules = ref<ModuleDescriptor[]>([]),
  document = ref<FlowDocument>(emptyDocument()),
  snapshot = ref<DocumentSnapshot | null>(null)
const runtime = ref<RuntimeSnapshot | null>(null),
  activeGraph = ref<RuntimeGraph | null>(null),
  online = ref(false),
  tick = ref(0)
const selectedNodeId = ref<string | null>(null),
  selectedEdgeId = ref<string | null>(null),
  selectedFilterId = ref<string | null>(null)
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
  inspectionTab = ref('stream')
const applyPreview = ref<{ started: string[]; kept: string[]; stopped: string[] } | null>(null)
const filterDialog = ref(false),
  filterName = ref('')
const defaultDirectory = ref(''),
  pathDialog = ref(false),
  pathValue = ref(''),
  pathMode = ref<'new' | 'open' | 'save-as'>('new')
let pendingPathResolve: ((path: string | null) => void) | null = null
let adopting = false,
  saveTimer: ReturnType<typeof setTimeout> | undefined,
  savingPromise: Promise<void> | null = null,
  hub: HubConnection | null = null
let polling: ReturnType<typeof setInterval> | undefined
const sameDocument = computed(
  () => !!snapshot.value && runtime.value?.documentId === snapshot.value.id,
)
const displayDocument = computed(() =>
  mode.value === 'flow' &&
  sameDocument.value &&
  runtime.value?.running &&
  activeGraph.value?.documentId === snapshot.value?.id
    ? activeGraph.value!.document
    : document.value,
)
const node = computed(() => displayDocument.value.nodes.find((n) => n.id === selectedNodeId.value))
const edge = computed(() => document.value.edges.find((e) => e.id === selectedEdgeId.value))
const namedFilter = computed(() =>
  document.value.filters.find((f) => f.id === (edge.value?.filterId ?? selectedFilterId.value)),
)
const filterUses = computed(
  () => document.value.edges.filter((e) => e.filterId === namedFilter.value?.id).length,
)
const definition = computed(() => modules.value.find((m) => m.type === node.value?.type))
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
const fileName = computed(() => snapshot.value?.path.split(/[\\/]/).pop() ?? 'RadioSender')
const visibleModules = computed(() =>
  modules.value.filter((m) =>
    (m.name + m.description).toLowerCase().includes(librarySearch.value.toLowerCase()),
  ),
)
const inspectorNode = computed(() =>
  sameDocument.value && node.value && definition.value
    ? { id: node.value.id, name: node.value.name, category: definition.value.category }
    : null,
)
const edgeState = computed(() =>
  sameDocument.value ? runtime.value?.edges.find((e) => e.id === edge.value?.id) : undefined,
)

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
watch(
  [() => runtime.value?.sessionId, () => runtime.value?.revision, () => runtime.value?.documentId],
  async () => {
    const state = runtime.value
    if (!state?.documentId) return
    try {
      const graph = await api<RuntimeGraph>('/runtime/graph')
      if (
        runtime.value?.sessionId === state.sessionId &&
        runtime.value?.revision === graph.revision
      )
        activeGraph.value = graph
    } catch (e) {
      error.value = (e as Error).message
    }
  },
)
function adopt(value: DocumentSnapshot) {
  adopting = true
  snapshot.value = value
  document.value = copy(value.document)
  document.value.filters ??= []
  editVersion.value = 0
  savedVersion.value = 0
  selectedNodeId.value = null
  selectedEdgeId.value = null
  selectedFilterId.value = null
  saveError.value = ''
  issues.value = []
  error.value = ''
  adopting = false
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
        snapshot.value = await api<DocumentSnapshot>(`/documents/${current.id}`, 'PUT', {
          revision: current.revision,
          document: copy(document.value),
        })
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
async function selectPath(action: 'new' | 'open' | 'save-as'): Promise<string | null> {
  pathMode.value = action
  const initial =
    action === 'open'
      ? (snapshot.value?.path ?? defaultDirectory.value)
      : `${defaultDirectory.value}/new-flow.radiosender.json`
  if (isDesktop) return chooseFile(action === 'open' ? 'open' : 'save', initial)
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
async function fileAction(action: 'new' | 'open' | 'save-as') {
  if (busy.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    if (action !== 'save-as') await flushSave()
    else {
      clearTimeout(saveTimer)
      if (savingPromise) await savingPromise.catch(() => {})
    }
    const path = await selectPath(action)
    if (!path) return
    if (action === 'save-as' && snapshot.value) {
      snapshot.value = await api<DocumentSnapshot>(
        `/documents/${snapshot.value.id}/save-as`,
        'POST',
        { revision: snapshot.value.revision, document: copy(document.value), path },
      )
      savedVersion.value = editVersion.value
      saveError.value = ''
    } else {
      adopt(
        await api<DocumentSnapshot>(`/documents/${action === 'new' ? 'new' : 'open'}`, 'POST', {
          path,
        }),
      )
      mode.value = action === 'new' ? 'editor' : 'flow'
    }
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
async function reload() {
  if (
    !snapshot.value ||
    !window.confirm('Reload the file from disk? Unsaved edits will be discarded.')
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
    x: 50 + (count % 3) * 350,
    y: 70 + Math.floor(count / 3) * 180,
  }
  selectNode(nodeId)
}
function selectNode(nodeId: string) {
  selectedNodeId.value = nodeId
  selectedEdgeId.value = null
  selectedFilterId.value = null
}
function selectEdge(edgeId: string) {
  if (mode.value !== 'editor') return
  selectedEdgeId.value = edgeId
  selectedNodeId.value = null
  selectedFilterId.value = null
}
function connect(connection: Connection) {
  if (mode.value !== 'editor' || !connection.source || !connection.target) return
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
    delayMs: 0,
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
function selectFilter(filterId: string) {
  selectedFilterId.value = filterId
  selectedNodeId.value = null
  selectedEdgeId.value = null
}
function newLibraryFilter() {
  selectedNodeId.value = null
  selectedEdgeId.value = null
  newFilter()
}
function deleteUnusedFilter() {
  document.value.filters = document.value.filters.filter((f) => f.id !== namedFilter.value?.id)
  selectedFilterId.value = null
}
function newFilter() {
  filterName.value = `Filter ${document.value.filters.length + 1}`
  filterDialog.value = true
}
function createFilter() {
  const name = filterName.value.trim()
  if (!name || document.value.filters.some((f) => f.name.toLowerCase() === name.toLowerCase()))
    return
  const filterId = id()
  document.value.filters.push({ id: filterId, name, rules: emptyFilter() })
  if (edge.value) edge.value.filterId = filterId
  else selectedFilterId.value = filterId
  filterDialog.value = false
}
async function prepareApply() {
  error.value = ''
  notice.value = ''
  busy.value = true
  try {
    await flushSave()
    issues.value = await api<FlowIssue[]>('/validate', 'POST', document.value)
    if (!issues.value.length && snapshot.value) {
      applyPreview.value = await api('/runtime/preview', 'POST', {
        documentId: snapshot.value.id,
        revision: snapshot.value.revision,
      })
      applyDialog.value = true
    } else mode.value = 'editor'
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
    activeGraph.value = await api<RuntimeGraph>('/runtime/graph')
    applyDialog.value = false
    mode.value = 'flow'
    notice.value = `Flow is running. ${result.restarted.length} node(s) started, ${result.kept.length} kept running.`
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
  notice.value = ''
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
    error.value = 'Saving failed. Retry Save or choose Save As before closing.'
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
    await refreshRuntime()
    if (runtime.value?.documentId)
      adopt(await api<DocumentSnapshot>(`/documents/${runtime.value.documentId}`))
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
  <v-app>
    <div class="app-shell">
      <header class="app-header">
        <a class="brand" href="/flows/index.html">RadioSender</a>
        <nav>
          <button :class="{ current: mode !== 'logs' }" @click="mode = 'flow'">Flow</button
          ><button :class="{ current: mode === 'logs' }" @click="mode = 'logs'">Logs</button>
        </nav>
        <span v-if="!isDesktop" class="connection-state"
          ><span class="state-dot" :class="{ on: online }"></span
          >{{ online ? 'Connected to RadioSender' : 'Reconnecting to RadioSender…' }}</span
        >
      </header>
      <div class="document-toolbar">
        <div class="document-title">
          <span class="panel-caption">{{
            mode === 'editor' ? 'Editor' : mode === 'logs' ? 'Application logs' : 'Flow'
          }}</span
          ><strong>{{ fileName }}</strong
          ><small v-if="snapshot" :title="snapshot.path">{{ snapshot.path }}</small>
        </div>
        <v-chip
          v-if="mode === 'editor'"
          :color="saveError ? 'error' : 'secondary'"
          size="x-small"
          >{{ saveLabel }}</v-chip
        >
        <div class="toolbar-actions">
          <v-btn :disabled="busy" :prepend-icon="mdiPlus" @click="fileAction('new')">New</v-btn
          ><v-btn :disabled="busy" :prepend-icon="mdiFolderOpenOutline" @click="fileAction('open')"
            >Open</v-btn
          ><template v-if="snapshot"
            ><v-btn :disabled="busy" :prepend-icon="mdiContentSaveOutline" @click="saveNow"
              >Save</v-btn
            ><v-btn :disabled="busy" @click="fileAction('save-as')">Save As</v-btn
            ><v-divider vertical class="mx-2" /><template v-if="mode === 'editor'"
              ><v-btn :prepend-icon="mdiArrowLeft" @click="mode = 'flow'">Back to Flow</v-btn
              ><v-btn
                color="primary"
                variant="flat"
                :loading="busy"
                :disabled="!online"
                @click="prepareApply"
                >Apply</v-btn
              ></template
            ><template v-else-if="mode === 'flow'"
              ><v-btn :prepend-icon="mdiPencilOutline" @click="mode = 'editor'">Edit</v-btn
              ><v-btn
                v-if="runtime?.running && sameDocument"
                :prepend-icon="mdiStop"
                :disabled="busy"
                @click="stop"
                >Stop</v-btn
              ><v-btn
                v-else
                color="primary"
                variant="flat"
                :prepend-icon="mdiPlay"
                :disabled="busy || !online"
                @click="prepareApply"
                >Start flow</v-btn
              ></template
            ></template
          >
        </div>
      </div>
      <div v-if="snapshot" class="runtime-strip">
        <span class="state-dot" :class="{ on: runtime?.running }"></span
        ><span v-if="runtime?.running"
          >Running: <strong>{{ runtime.path?.split(/[\\/]/).pop() }}</strong> · revision
          {{ runtime.revision }}</span
        ><span v-else>Flow stopped</span
        ><v-chip v-if="unapplied && mode === 'editor'" color="warning" size="x-small"
          >Unapplied changes</v-chip
        ><span class="runtime-note">{{
          mode === 'editor'
            ? 'Changes are saved automatically. Apply activates them.'
            : 'Select a node to use its controls, inspect events or read its logs.'
        }}</span>
      </div>
      <v-alert v-if="error || saveError || runtime?.error" type="error" class="app-alert"
        ><div class="alert-content">
          <span>{{ error || saveError || runtime?.error }}</span
          ><v-btn v-if="saveError" @click="saveNow">Retry save</v-btn
          ><v-btn v-if="saveError" @click="reload">Reload file</v-btn>
        </div></v-alert
      >
      <v-alert v-if="notice" :icon="false" class="app-alert" closable @click:close="notice = ''">{{
        notice
      }}</v-alert>
      <v-alert v-if="issues.length" type="warning" class="app-alert"
        ><strong>Resolve these issues before applying:</strong>
        <div class="validation-items">
          <v-btn
            v-for="issue in issues"
            :key="issue.elementId + issue.field"
            variant="text"
            @click="
              document.nodes.some((n) => n.id === issue.elementId)
                ? selectNode(issue.elementId)
                : document.filters.some((f) => f.id === issue.elementId)
                  ? ((selectedFilterId = issue.elementId),
                    (selectedNodeId = null),
                    (selectedEdgeId = null))
                  : selectEdge(issue.elementId)
            "
            >{{ issue.message }}</v-btn
          >
        </div></v-alert
      >
      <main v-if="mode === 'logs'" class="general-logs">
        <div class="page-heading">
          <h1>General logs</h1>
          <p>Messages that do not belong to a node. Enable node logs to see everything together.</p>
        </div>
        <LogViewer :tick="tick" />
      </main>
      <main v-else-if="!snapshot" class="welcome">
        <div class="welcome-card">
          <h1>Open or create a new flow</h1>
          <p>Open a saved configuration or create a new one.</p>
          <div class="welcome-actions">
            <v-btn size="default" :prepend-icon="mdiFolderOpenOutline" @click="fileAction('open')"
              >Open flow</v-btn
            ><v-btn
              size="default"
              color="primary"
              variant="flat"
              :prepend-icon="mdiPlus"
              @click="fileAction('new')"
              >Create new flow</v-btn
            >
          </div>
        </div>
      </main>
      <main v-else class="workspace" :class="mode">
        <aside v-if="mode === 'editor'" class="library">
          <div class="panel-heading">
            <h2>Module library</h2>
          </div>
          <v-text-field v-model="librarySearch" label="Search modules" class="library-search" />
          <template v-for="category in ['Source', 'Processor', 'Target']" :key="category"
            ><h3 class="category-heading">
              {{ category === 'Processor' ? 'Processing' : category + 's' }}
            </h3>
            <v-btn
              v-for="module in visibleModules.filter((m) => m.category === category)"
              :key="module.type"
              variant="text"
              class="library-module"
              @click="addNode(module)"
              ><span
                ><strong>{{ module.name }}</strong
                ><small>{{ module.description }}</small></span
              ></v-btn
            ></template
          >
          <h3 class="category-heading">Reusable filters</h3>
          <v-btn
            v-for="filter in document.filters"
            :key="filter.id"
            class="filter-library-item"
            variant="text"
            @click="selectFilter(filter.id)"
            >{{ filter.name }}</v-btn
          ><v-btn
            class="new-filter-button"
            :prepend-icon="mdiPlus"
            variant="text"
            @click="newLibraryFilter"
            >New filter</v-btn
          >
        </aside>
        <div class="flow-workspace">
          <div class="canvas-toolbar">
            <span
              >{{ displayDocument.nodes.length }} nodes ·
              {{ displayDocument.edges.length }} connections</span
            ><span>{{
              mode === 'editor'
                ? 'Drag between ports to connect'
                : runtime?.running && sameDocument
                  ? 'Live flow'
                  : 'Flow preview'
            }}</span>
          </div>
          <FlowCanvas
            :key="snapshot.id + mode"
            :document="displayDocument"
            :modules="modules"
            :runtime="runtime"
            :same-document="sameDocument"
            :selected-node="selectedNodeId"
            :selected-edge="selectedEdgeId"
            :read-only="mode !== 'editor'"
            @node="selectNode"
            @edge="selectEdge"
            @connect="connect"
            @move="
              (nodeId, x, y) => {
                if (mode === 'editor') document.editor.positions[nodeId] = { x, y }
              }
            "
            @viewport="
              (value) => {
                if (mode === 'editor') document.editor.viewport = value
              }
            "
          />
          <section v-if="mode === 'flow' && inspectorNode" class="inspection-area">
            <v-tabs v-model="inspectionTab" density="compact" height="38" color="primary"
              ><v-tab value="stream">Stream inspector</v-tab
              ><v-tab value="logs">Node logs</v-tab></v-tabs
            ><NodeInspector
              v-show="inspectionTab === 'stream'"
              :key="inspectorNode.id"
              :node-id="inspectorNode.id"
              :name="inspectorNode.name"
              :category="inspectorNode.category"
              :runtime="runtime"
              :tick="tick"
            /><LogViewer
              v-if="inspectionTab === 'logs'"
              :node-id="inspectorNode.id"
              :session-id="runtime?.sessionId"
              :tick="tick"
            />
          </section>
          <div v-else-if="mode === 'flow'" class="inspector-placeholder">
            {{
              sameDocument
                ? 'Select a node to inspect its stream and logs.'
                : 'Start this flow to use its controls and inspect events.'
            }}
          </div>
        </div>
        <aside class="properties">
          <template v-if="mode === 'editor' && node"
            ><div class="panel-heading">
              <span class="panel-caption">Node configuration</span>
              <h2>{{ definition?.name ?? 'Unavailable module' }}</h2>
            </div>
            <div class="properties-content">
              <v-text-field v-model="node.name" label="Name" maxlength="100" /><v-checkbox
                v-model="node.enabled"
                label="Enabled"
              /><SettingsForm
                v-if="definition"
                :node="node"
                :definition="definition"
                @update="(key, value) => (node!.settings[key] = value)"
              />
              <p v-if="definition?.category === 'Processor'" class="hint">
                Passes events through unchanged. Use it as a named inspection or branching point.
              </p>
              <p v-if="definition?.view === 'manual-input'" class="hint">
                Manual entry controls are available on the Flow screen after applying.
              </p>
              <v-btn color="error" variant="text" block @click="removeSelection">Remove node</v-btn>
            </div></template
          >
          <template v-else-if="mode === 'editor' && (edge || namedFilter)"
            ><div class="panel-heading">
              <span class="panel-caption">{{
                edge ? 'Connection configuration' : 'Reusable filter'
              }}</span>
              <h2>{{ edge ? 'Branch settings' : namedFilter?.name }}</h2>
            </div>
            <div class="properties-content">
              <template v-if="edge"
                ><p class="connection-description">
                  {{ document.nodes.find((n) => n.id === edge!.from.node)?.name }} →
                  {{ document.nodes.find((n) => n.id === edge!.to.node)?.name }}
                </p>
                <v-checkbox v-model="edge.enabled" label="Connection enabled" /><v-text-field
                  :model-value="edge.delayMs ?? 0"
                  type="number"
                  min="0"
                  max="60000"
                  label="Delay (ms)"
                  hint="Adds latency only to this branch. 0 sends immediately."
                  persistent-hint
                  @update:model-value="edge.delayMs = Number($event)"
                /><v-select
                  v-model="edge.filterId"
                  :items="document.filters"
                  item-title="name"
                  item-value="id"
                  clearable
                  label="Filter"
                  placeholder="No filter"
                /><v-btn :prepend-icon="mdiPlus" block @click="newFilter">Create filter</v-btn>
                <p v-if="edgeState" class="hint">
                  {{ edgeState.forwarded }} forwarded · {{ edgeState.filtered }} filtered ·
                  {{ edgeState.pending }} delayed
                </p></template
              ><template v-if="namedFilter"
                ><v-divider class="my-5" /><v-text-field
                  v-model="namedFilter.name"
                  label="Filter name"
                  maxlength="100"
                />
                <p class="hint">
                  Used by {{ filterUses }} connection(s). Changes apply everywhere this filter is
                  selected.
                </p>
                <FilterEditor :key="namedFilter.id" v-model="namedFilter.rules" /><v-btn
                  v-if="!filterUses"
                  color="error"
                  variant="text"
                  block
                  @click="deleteUnusedFilter"
                  >Delete unused filter</v-btn
                ></template
              ><v-btn v-if="edge" color="error" variant="text" block @click="removeSelection"
                >Remove connection</v-btn
              >
            </div></template
          >
          <template v-else-if="mode === 'flow' && node"
            ><div class="panel-heading">
              <span class="panel-caption">{{ definition?.category }} controls</span>
              <h2>{{ node.name }}</h2>
            </div>
            <div class="properties-content">
              <div class="status-card">
                <span class="node-status">Status: {{ nodeState?.status ?? 'Not running' }}</span>
                <small v-if="nodeState?.detail">{{ nodeState.detail }}</small
                ><small v-if="nodeState?.pending"
                  >{{ nodeState.pending }} events pending delivery</small
                >
              </div>
              <ModuleControls
                v-if="definition && runtime"
                :definition="definition"
                :key="node.id"
                :node-id="node.id"
                :runtime="runtime"
                :disabled="!sameDocument || !nodeState || !runtime.running"
                @changed="refreshRuntime"
              />
              <p v-else class="hint">
                Inspect this node's data and logs below the graph. Connection settings are available
                in the editor.
              </p>
            </div></template
          >
          <div v-else class="properties-empty">
            <h2>{{ mode === 'editor' ? 'Configure your flow' : 'Select a node' }}</h2>
            <p>
              {{
                mode === 'editor'
                  ? 'Select a node for its settings or a connection for a named filter and delay.'
                  : 'Use module controls, inspect incoming and outgoing events, or view node logs.'
              }}
            </p>
          </div>
        </aside>
      </main>
      <v-dialog :model-value="pathDialog" max-width="540" persistent
        ><v-card
          ><v-card-title>{{
            pathMode === 'open'
              ? 'Open configuration'
              : pathMode === 'new'
                ? 'New configuration'
                : 'Save configuration as'
          }}</v-card-title
          ><v-card-text
            ><p class="hint">
              Choose a JSON file on this RadioSender computer. New files start autosaving
              immediately.
            </p>
            <v-text-field
              v-model="pathValue"
              label="Full JSON file path"
              autofocus
              @keydown.enter="finishPath(pathValue)"
            /><small
              >Choose an unused name when creating a file or saving a copy.</small
            ></v-card-text
          ><v-card-actions
            ><v-btn @click="finishPath(null)">Cancel</v-btn
            ><v-btn color="primary" variant="flat" @click="finishPath(pathValue)">{{
              pathMode === 'open' ? 'Open file' : 'Save file'
            }}</v-btn></v-card-actions
          ></v-card
        ></v-dialog
      >
      <v-dialog v-model="filterDialog" max-width="420"
        ><v-card
          ><v-card-title>Create reusable filter</v-card-title
          ><v-card-text
            ><v-text-field
              v-model="filterName"
              label="Filter name"
              maxlength="100"
              autofocus
              @keydown.enter="createFilter"
            />
            <p class="hint">
              Choose a meaningful name. It will appear on each connection using this filter.
            </p></v-card-text
          ><v-card-actions
            ><v-btn @click="filterDialog = false">Cancel</v-btn
            ><v-btn
              color="primary"
              variant="flat"
              :disabled="
                !filterName.trim() ||
                document.filters.some(
                  (f) => f.name.toLowerCase() === filterName.trim().toLowerCase(),
                )
              "
              @click="createFilter"
              >Create</v-btn
            ></v-card-actions
          ></v-card
        ></v-dialog
      >
      <v-dialog v-model="applyDialog" max-width="520" :persistent="busy"
        ><v-card
          ><v-card-title>{{
            mode === 'editor' ? 'Apply configuration' : 'Start flow'
          }}</v-card-title
          ><v-card-text
            ><p>
              {{ document.nodes.filter((n) => n.enabled).length }} enabled nodes and
              {{ document.edges.filter((e) => e.enabled).length }} enabled connections.
            </p>
            <div v-if="applyPreview" class="apply-preview">
              <p v-if="applyPreview.started.length">
                <strong>Start / restart:</strong> {{ applyPreview.started.join(', ') }}
              </p>
              <p v-if="applyPreview.stopped.length">
                <strong>Stop:</strong> {{ applyPreview.stopped.join(', ') }}
              </p>
              <p v-if="applyPreview.kept.length">
                <strong>Keep running:</strong> {{ applyPreview.kept.join(', ') }}
              </p>
            </div>
            <p class="hint">
              Changed connection settings restart the affected nodes. Unchanged connections keep
              running. Accepted events, including delayed events, finish before switching.
            </p>
            <p v-if="runtime?.running && !sameDocument" class="hint">
              This replaces the currently running document and starts a new inspection session.
            </p></v-card-text
          ><v-card-actions
            ><v-btn :disabled="busy" @click="applyDialog = false">Cancel</v-btn
            ><v-btn color="primary" variant="flat" :loading="busy" @click="apply"
              >Apply configuration</v-btn
            ></v-card-actions
          ></v-card
        ></v-dialog
      >
    </div>
  </v-app>
</template>
