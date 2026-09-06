<script setup lang="ts">
import { reactive, ref } from 'vue'
import { api } from '../api'
import type { RuntimeSnapshot } from '../types'
const props = defineProps<{ nodeId: string; runtime: RuntimeSnapshot; disabled: boolean }>()
const emit = defineEmits<{ sent: [] }>()
const values = reactive({
  competitorId: '',
  competitorIdType: 'PunchingCard',
  control: 35,
  controlType: 'Unknown',
  competitorStatus: 'Unknown',
  cancellation: false,
  netTime: false,
  time: localNow(),
})
const useNow = ref(true),
  busy = ref(false),
  message = ref(''),
  failed = ref(false)
function localNow() {
  const d = new Date()
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 23)
}
async function send() {
  busy.value = true
  message.value = ''
  failed.value = false
  try {
    if (useNow.value) values.time = localNow()
    await api(`/nodes/${props.nodeId}/commands/send`, 'POST', {
      sessionId: props.runtime.sessionId,
      revision: props.runtime.revision,
      arguments: {
        punch: {
          ...values,
          time: values.time,
          sourceId: props.nodeId,
          receivedAt: new Date().toISOString(),
        },
      },
    })
    message.value = 'Event accepted. Check target delivery in the inspector.'
    emit('sent')
  } catch (error) {
    failed.value = true
    message.value = String(error instanceof Error ? error.message : error)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <form class="manual-form" @submit.prevent="send">
    <h3>Send an event</h3>
    <p v-if="disabled" class="hint">Start this flow to send events.</p>
    <v-text-field
      v-model="values.competitorId"
      label="Competitor ID"
      required
      maxlength="256"
      placeholder="Card or bib number"
    />
    <v-select
      v-model="values.competitorIdType"
      label="Identifier type"
      :items="['PunchingCard', 'BibNumber', 'TimingTransponder', 'Unknown']"
    />
    <div class="field-pair">
      <v-text-field
        v-model.number="values.control"
        label="Control"
        type="number"
        required
        min="0"
      /><v-select
        v-model="values.controlType"
        label="Control type"
        :items="['Unknown', 'Control', 'Start', 'Finish', 'Check', 'Clear']"
      />
    </div>
    <v-checkbox v-model="useNow" label="Use current time" />
    <v-text-field
      v-if="!useNow"
      v-model="values.time"
      label="Event time"
      type="datetime-local"
      step="0.001"
      required
    />
    <v-select
      v-model="values.competitorStatus"
      label="Competitor status"
      :items="['Unknown', 'OK', 'DNS', 'DNF', 'MP', 'DSQ', 'OverTime', 'WaitingStart', 'Running']"
    />
    <v-checkbox v-model="values.cancellation" label="Cancellation" /><v-checkbox
      v-model="values.netTime"
      label="Net time"
    />
    <v-btn type="submit" color="primary" variant="flat" block :disabled="disabled" :loading="busy"
      >Send event</v-btn
    >
    <v-alert
      v-if="message"
      :type="failed ? 'error' : undefined"
      :icon="failed ? undefined : false"
      class="mt-4"
      >{{ message }}</v-alert
    >
  </form>
</template>
