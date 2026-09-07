export type SettingValue = string | number | boolean | null | (string | number)[]
export interface FlowNode {
  id: string
  type: string
  name: string
  enabled: boolean
  settings: Record<string, SettingValue>
}
export interface EdgeFilter {
  enabled: boolean
  mapControls: Record<string, number>
  mapCompetitorIds: Record<string, string>
  includeOnlyControls: number[]
  includeOnlyCompetitorIds: string[]
  typeFromCode: Record<string, number[]>
  overrideCompetitorIdType: string | null
  ignoreOlderThanSeconds: number
}
export interface FlowEdge {
  id: string
  from: { node: string; port: string }
  to: { node: string; port: string }
  enabled: boolean
  filterId?: string | null
  delayMs: number
}
export interface NamedFlowFilter {
  id: string
  name: string
  rules: EdgeFilter
}
export interface FlowDocument {
  schemaVersion: number
  nodes: FlowNode[]
  edges: FlowEdge[]
  filters: NamedFlowFilter[]
  editor: {
    positions: Record<string, { x: number; y: number }>
    viewport?: { x: number; y: number; zoom: number } | null
  }
}
export interface DocumentSnapshot {
  id: string
  path: string
  revision: number
  document: FlowDocument
}
export interface ModuleField {
  key: string
  label: string
  kind: string
  required: boolean
  min: number | null
  max: number | null
  choices?: string[] | null
  itemKind?: string | null
  help: string | null
}
export interface ModuleDescriptor {
  type: string
  name: string
  category: string
  description: string
  inputs: string[]
  outputs: string[]
  defaults: FlowNode['settings']
  fields: ModuleField[]
  commands: { id: string; label: string }[]
  view: string | null
}
export interface RuntimeNode {
  id: string
  name: string
  type: string
  status: string
  detail: string | null
  pending: number
}
export interface RuntimeSnapshot {
  sessionId: string
  documentId: string | null
  path: string | null
  revision: number
  running: boolean
  error: string | null
  nodes: RuntimeNode[]
  edges: { id: string; forwarded: number; filtered: number; pending: number; rejected: number }[]
}
export interface RuntimeGraph {
  documentId: string | null
  path: string | null
  revision: number
  document: FlowDocument
}
export interface Punch {
  competitorId: string
  competitorIdType: string
  control: number
  controlType: string
  time: string
  sourceId: string
  receivedAt: string
  competitorStatus: string
  cancellation: boolean
  competitor?: Record<string, string | null> | null
  netTime: boolean
}
export interface Observation {
  id: number
  eventId: string
  executionId: string
  replayOf: number | null
  nodeId: string
  portId: string
  direction: string
  edgeId: string | null
  revision: number
  observedAt: string
  punch: Punch
  status: string
  detail: string | null
}
export interface ObservationPage {
  cursor: number
  historyExpired: boolean
  evicted: number
  items: Observation[]
}
export interface FlowIssue {
  elementId: string
  field: string
  message: string
}
export const emptyDocument = (): FlowDocument => ({
  schemaVersion: 1,
  nodes: [],
  edges: [],
  filters: [],
  editor: { positions: {} },
})
export const emptyFilter = (): EdgeFilter => ({
  enabled: true,
  mapControls: {},
  mapCompetitorIds: {},
  includeOnlyControls: [],
  includeOnlyCompetitorIds: [],
  typeFromCode: {},
  overrideCompetitorIdType: null,
  ignoreOlderThanSeconds: 0,
})
export const id = () => crypto.randomUUID()
export const copy = <T>(value: T): T => JSON.parse(JSON.stringify(value))
