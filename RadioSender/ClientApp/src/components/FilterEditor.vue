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
function list(event: Event, key: 'includeOnlyControls' | 'includeOnlyCompetitorIds') {
  const values = (event.target as HTMLInputElement).value
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
</script>

<template>
  <div class="filter-editor">
    <label class="field check"
      ><span>Enable filter & mapping</span
      ><input
        type="checkbox"
        :checked="modelValue.enabled"
        @change="update('enabled', ($event.target as HTMLInputElement).checked)"
    /></label>
    <p class="hint">
      Mappings run before the inclusion checks. Each connection processes its own copy.
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
    <label class="field"
      ><span>Include only controls</span
      ><input
        :value="modelValue.includeOnlyControls?.join(', ')"
        placeholder="All controls"
        @change="list($event, 'includeOnlyControls')"
      /><small>Comma-separated codes, after mapping.</small></label
    >
    <label class="field"
      ><span>Include only identifiers</span
      ><input
        :value="modelValue.includeOnlyCompetitorIds?.join(', ')"
        placeholder="All identifiers"
        @change="list($event, 'includeOnlyCompetitorIds')"
    /></label>
    <small v-if="error" class="error-text">{{ error }}</small>
    <label class="field"
      ><span>Maximum age (seconds)</span
      ><input
        type="number"
        min="0"
        max="315360000"
        :value="modelValue.ignoreOlderThanSeconds ?? 0"
        @input="update('ignoreOlderThanSeconds', Number(($event.target as HTMLInputElement).value))"
      /><small>0 keeps every event. Net times are exempt.</small></label
    >
    <label class="field"
      ><span>Identifier type override</span
      ><select
        :value="modelValue.overrideCompetitorIdType ?? ''"
        @change="
          update('overrideCompetitorIdType', ($event.target as HTMLSelectElement).value || null)
        "
      >
        <option value="">Keep original</option>
        <option>BibNumber</option>
        <option>PunchingCard</option>
        <option>TimingTransponder</option>
        <option>Unknown</option>
      </select></label
    >
    <details>
      <summary>Control types from codes</summary>
      <label
        v-for="type in ['Start', 'Finish', 'Control', 'Check', 'Clear']"
        :key="type"
        class="field"
        ><span>{{ type }}</span
        ><input
          :value="modelValue.typeFromCode?.[type]?.join(', ') ?? ''"
          placeholder="Control codes"
          @change="
            (event) => {
              const text = (event.target as HTMLInputElement).value
              const values = text
                .split(',')
                .map((s) => s.trim())
                .filter(Boolean)
              if (values.every((s) => /^-?\d+$/.test(s)))
                update('typeFromCode', { ...modelValue.typeFromCode, [type]: values.map(Number) })
              else error = 'Control codes must be integers.'
            }
          "
      /></label>
    </details>
  </div>
</template>
