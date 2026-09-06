<script setup lang="ts">
import type { FlowNode, ModuleDescriptor } from '../types'
defineProps<{ node: FlowNode; definition: ModuleDescriptor }>()
const emit = defineEmits<{ update: [key: string, value: string | number | boolean] }>()
</script>
<template>
  <div class="settings-form">
    <template v-for="field in definition.fields" :key="field.key">
      <v-switch
        v-if="field.kind === 'boolean'"
        :label="field.label"
        :model-value="Boolean(node.settings[field.key])"
        @update:model-value="emit('update', field.key, Boolean($event))"
      />
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
        :type="field.kind === 'number' ? 'number' : 'text'"
        :min="field.min ?? undefined"
        :max="field.max ?? undefined"
        :required="field.required"
        :hint="field.help ?? undefined"
        persistent-hint
        :model-value="node.settings[field.key]"
        @update:model-value="
          emit(
            'update',
            field.key,
            field.kind === 'number' && $event !== '' ? Number($event) : $event,
          )
        "
      />
    </template>
  </div>
</template>
