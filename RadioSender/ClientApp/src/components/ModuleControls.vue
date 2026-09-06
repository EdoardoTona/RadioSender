<script setup lang="ts">
import { computed, ref } from 'vue'
import { api } from '../api'
import { moduleViews } from '../modules/views'
import type { ModuleDescriptor, RuntimeSnapshot } from '../types'
const props = defineProps<{
  nodeId: string
  definition: ModuleDescriptor
  runtime: RuntimeSnapshot
  disabled: boolean
}>()
const emit = defineEmits<{ changed: [] }>()
const view = computed(() =>
  props.definition.view ? moduleViews[props.definition.view] : undefined,
)
const busy = ref(false),
  message = ref(''),
  error = ref('')
async function run(command: string) {
  busy.value = true
  error.value = ''
  message.value = ''
  try {
    const result = await api<{ message: string }>(
      `/nodes/${props.nodeId}/commands/${command}`,
      'POST',
      {
        sessionId: props.runtime.sessionId,
        revision: props.runtime.revision,
        arguments: {},
      },
    )
    message.value = result.message
    emit('changed')
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
</script>
<template>
  <component
    :is="view"
    v-if="view"
    :node-id="nodeId"
    :runtime="runtime"
    :disabled="disabled"
    @sent="emit('changed')"
  />
  <div v-else class="module-commands">
    <v-btn
      v-for="command in definition.commands"
      :key="command.id"
      :disabled="disabled || busy"
      block
      @click="run(command.id)"
      >{{ command.label }}</v-btn
    >
    <p v-if="!definition.commands.length" class="hint">
      Inspect this node's data and logs below the graph. Connection settings are available in the
      editor.
    </p>
    <v-alert v-if="error" type="error">{{ error }}</v-alert
    ><v-alert v-if="message" :icon="false">{{ message }}</v-alert>
  </div>
</template>
