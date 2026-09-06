<script setup lang="ts">
import type { FlowNode, ModuleDescriptor } from '../types'
defineProps<{ node: FlowNode; definition: ModuleDescriptor }>()
const emit = defineEmits<{ update: [key: string, value: string | number | boolean] }>()
function value(event: Event, kind: string) {
  const input = event.target as HTMLInputElement
  return kind === 'boolean'
    ? input.checked
    : kind === 'number' && input.value !== ''
      ? Number(input.value)
      : input.value
}
</script>

<template>
  <div class="settings-form">
    <label
      v-for="field in definition.fields"
      :key="field.key"
      class="field"
      :class="{ check: field.kind === 'boolean' }"
    >
      <span>{{ field.label }}<span v-if="field.required" class="required"> *</span></span>
      <input
        v-if="field.kind === 'boolean'"
        type="checkbox"
        :checked="Boolean(node.settings[field.key])"
        @change="emit('update', field.key, value($event, field.kind))"
      />
      <textarea
        v-else-if="field.key === 'format'"
        rows="3"
        :value="String(node.settings[field.key] ?? '')"
        @input="emit('update', field.key, ($event.target as HTMLTextAreaElement).value)"
      ></textarea>
      <input
        v-else
        :type="field.kind === 'number' ? 'number' : 'text'"
        :min="field.min ?? undefined"
        :max="field.max ?? undefined"
        :required="field.required"
        :value="node.settings[field.key]"
        @input="emit('update', field.key, value($event, field.kind))"
      />
      <small v-if="field.help">{{ field.help }}</small>
    </label>
  </div>
</template>
