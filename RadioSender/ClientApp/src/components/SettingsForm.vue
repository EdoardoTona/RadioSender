<script setup lang="ts">
import type { FlowNode, ModuleDescriptor, SettingValue } from '../types'
const props = defineProps<{ node: FlowNode; definition: ModuleDescriptor; nodes: FlowNode[] }>()
const emit = defineEmits<{ update: [key: string, value: SettingValue] }>()
function list(key: string): (string | number)[] {
  const value = props.node.settings[key]
  return Array.isArray(value) ? value : []
}
function setItem(key: string, index: number, value: string | number) {
  const values = [...list(key)]
  values[index] = value
  emit('update', key, values)
}
</script>
<template>
  <div class="settings-form">
    <template v-for="field in definition.fields" :key="field.key">
      <v-checkbox
        v-if="field.kind === 'boolean'"
        :label="field.label"
        :model-value="Boolean(node.settings[field.key])"
        @update:model-value="emit('update', field.key, Boolean($event))"
      />
      <v-select
        v-else-if="field.kind === 'select' || field.kind === 'provider'"
        :label="field.label"
        :items="
          field.kind === 'provider'
            ? nodes
                .filter((n) => n.type === 'provider.oribos' && n.enabled)
                .map((n) => ({ title: n.name, value: n.id }))
            : (field.choices ?? [])
        "
        :model-value="node.settings[field.key]"
        :hint="
          field.kind === 'provider'
            ? 'Add an Oribos data provider to share its lookup between nodes.'
            : (field.help ?? undefined)
        "
        persistent-hint
        @update:model-value="emit('update', field.key, $event)"
      />
      <fieldset v-else-if="field.kind === 'list'" class="setting-list">
        <legend>{{ field.label }}</legend>
        <div v-for="(value, index) in list(field.key)" :key="index" class="setting-list-row">
          <v-select
            v-if="field.choices"
            :items="field.choices"
            :model-value="String(value)"
            :aria-label="`${field.label} ${index + 1}`"
            @update:model-value="setItem(field.key, index, $event ?? '')"
          />
          <v-text-field
            v-else
            :model-value="value"
            :type="field.itemKind === 'number' ? 'number' : 'text'"
            :aria-label="`${field.label} ${index + 1}`"
            @update:model-value="
              setItem(field.key, index, field.itemKind === 'number' ? Number($event) : $event)
            "
          />
          <v-btn
            :aria-label="`Remove ${field.label} ${index + 1}`"
            @click="
              emit(
                'update',
                field.key,
                list(field.key).filter((_, i) => i !== index),
              )
            "
            >Remove</v-btn
          >
        </div>
        <v-btn
          @click="
            emit('update', field.key, [
              ...list(field.key),
              field.choices?.[0] ?? (field.itemKind === 'number' ? 1 : ''),
            ])
          "
          >Add item</v-btn
        >
        <p v-if="field.help" class="hint">{{ field.help }}</p>
      </fieldset>
      <v-textarea
        v-else-if="field.key === 'format'"
        :label="field.label"
        :rows="3"
        :model-value="String(node.settings[field.key] ?? '')"
        :hint="field.help ?? undefined"
        persistent-hint
        @update:model-value="emit('update', field.key, $event)"
      />
      <v-text-field
        v-else
        :label="field.label"
        :type="['number', 'password'].includes(field.kind) ? field.kind : 'text'"
        :min="field.min ?? undefined"
        :max="field.max ?? undefined"
        :required="field.required"
        :hint="field.help ?? undefined"
        persistent-hint
        :model-value="node.settings[field.key]"
        autocomplete="off"
        @update:model-value="
          emit(
            'update',
            field.key,
            field.kind === 'number' ? ($event === '' ? null : Number($event)) : $event,
          )
        "
      />
    </template>
  </div>
</template>
<style scoped>
.setting-list {
  border: 1px solid #d6d6d6;
  padding: 10px;
  min-width: 0;
}
.setting-list legend {
  padding: 0 4px;
  font-size: 12px;
}
.setting-list-row {
  display: flex;
  gap: 6px;
  align-items: center;
  margin-bottom: 8px;
}
.setting-list-row > :first-child {
  min-width: 0;
  flex: 1;
}
</style>
