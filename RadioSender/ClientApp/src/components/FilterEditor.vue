<script setup lang="ts">
import { ref } from 'vue'
import { emptyFilter, type EdgeFilter } from '../types'
import MappingTable from './MappingTable.vue'
const props = defineProps<{ modelValue: EdgeFilter }>()
const emit = defineEmits<{ 'update:modelValue': [value: EdgeFilter] }>()
const error = ref('')
function update(key: keyof EdgeFilter, value: unknown) {
  emit('update:modelValue', { ...emptyFilter(), ...props.modelValue, [key]: value })
}
function list(text: string, key: 'includeOnlyControls' | 'includeOnlyCompetitorIds') {
  const values = text
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean)
  if (key === 'includeOnlyControls' && values.some((s) => !/^-?\d+$/.test(s))) {
    error.value = 'Control codes must be comma-separated integers.'
    return
  }
  error.value = ''
  update(key, key === 'includeOnlyControls' ? values.map(Number) : values)
}
function setType(type: string, text: string) {
  const values = text
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean)
  if (values.every((s) => /^-?\d+$/.test(s))) {
    error.value = ''
    update('typeFromCode', { ...props.modelValue.typeFromCode, [type]: values.map(Number) })
  } else error.value = 'Control codes must be integers.'
}
</script>
<template>
  <div class="filter-editor">
    <v-switch
      label="Enable filter & mapping"
      :model-value="modelValue.enabled"
      @update:model-value="update('enabled', Boolean($event))"
    />
    <p class="hint">
      Mappings run before inclusion checks. Every connection processes its own copy.
    </p>
    <MappingTable
      label="Control mapping"
      :model-value="modelValue.mapControls ?? {}"
      numeric
      @update:model-value="update('mapControls', $event)"
    />
    <MappingTable
      label="Identifier mapping"
      :model-value="modelValue.mapCompetitorIds ?? {}"
      @update:model-value="update('mapCompetitorIds', $event)"
    />
    <v-text-field
      label="Include only controls"
      :model-value="modelValue.includeOnlyControls?.join(', ')"
      placeholder="All controls"
      hint="Comma-separated codes, after mapping."
      persistent-hint
      @change="list(($event.target as HTMLInputElement).value, 'includeOnlyControls')"
    />
    <v-text-field
      label="Include only identifiers"
      :model-value="modelValue.includeOnlyCompetitorIds?.join(', ')"
      placeholder="All identifiers"
      @change="list(($event.target as HTMLInputElement).value, 'includeOnlyCompetitorIds')"
    />
    <small v-if="error" class="error-text">{{ error }}</small>
    <v-text-field
      label="Maximum age (seconds)"
      type="number"
      min="0"
      max="315360000"
      :model-value="modelValue.ignoreOlderThanSeconds ?? 0"
      hint="0 keeps every event. Net times are exempt."
      persistent-hint
      @update:model-value="update('ignoreOlderThanSeconds', Number($event))"
    />
    <v-select
      label="Identifier type override"
      :model-value="modelValue.overrideCompetitorIdType"
      :items="['BibNumber', 'PunchingCard', 'TimingTransponder', 'Unknown']"
      clearable
      placeholder="Keep original"
      @update:model-value="update('overrideCompetitorIdType', $event)"
    />
    <details>
      <summary>Control types from codes</summary>
      <v-text-field
        v-for="type in ['Start', 'Finish', 'Control', 'Check', 'Clear']"
        :key="type"
        :label="type"
        :model-value="modelValue.typeFromCode?.[type]?.join(', ') ?? ''"
        placeholder="Control codes"
        @change="setType(type, ($event.target as HTMLInputElement).value)"
      />
    </details>
  </div>
</template>
