<script setup lang="ts">
import { ref } from 'vue'
const props = defineProps<{
  label: string
  modelValue: Record<string, string | number>
  numeric?: boolean
}>()
const emit = defineEmits<{ 'update:modelValue': [value: Record<string, string | number>] }>()
const from = ref(''),
  to = ref(''),
  error = ref('')
function add() {
  error.value = ''
  if (
    !from.value.trim() ||
    (props.numeric && (!/^-?\d+$/.test(from.value) || !/^-?\d+$/.test(to.value)))
  ) {
    error.value = props.numeric ? 'Enter integer control codes.' : 'Enter an original identifier.'
    return
  }
  emit('update:modelValue', {
    ...props.modelValue,
    [from.value.trim()]: props.numeric ? Number(to.value) : to.value,
  })
  from.value = ''
  to.value = ''
}
function remove(key: string) {
  const next = { ...props.modelValue }
  delete next[key]
  emit('update:modelValue', next)
}
</script>
<template>
  <div class="mapping-table">
    <h4>{{ label }}</h4>
    <div v-for="(value, key) in modelValue" :key="key" class="mapping-row">
      <code>{{ key }}</code
      ><span>→</span><code>{{ value === '' ? '(drop)' : value }}</code
      ><v-btn variant="text" :aria-label="`Remove mapping ${key}`" @click="remove(String(key))"
        >×</v-btn
      >
    </div>
    <form class="mapping-row new-mapping" @submit.prevent="add">
      <v-text-field v-model="from" :aria-label="`${label} from`" placeholder="From" /><span>→</span
      ><v-text-field v-model="to" :aria-label="`${label} to`" placeholder="To" /><v-btn
        type="submit"
        :aria-label="`Add ${label.toLowerCase()}`"
        >+</v-btn
      >
    </form>
    <small v-if="error" class="error-text">{{ error }}</small
    ><small v-else>{{
      numeric ? 'Map a positive control to 0 to drop it.' : 'An empty destination drops the event.'
    }}</small>
  </div>
</template>
