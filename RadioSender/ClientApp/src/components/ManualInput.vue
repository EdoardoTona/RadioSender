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
      punch: {
        ...values,
        time: values.time,
        sourceId: props.nodeId,
        receivedAt: new Date().toISOString(),
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
    <p v-if="disabled" class="hint">Apply this document and start the node to send.</p>
    <label class="field"
      ><span>Competitor ID</span
      ><input
        v-model="values.competitorId"
        required
        maxlength="256"
        placeholder="Card or bib number"
    /></label>
    <label class="field"
      ><span>Identifier type</span
      ><select v-model="values.competitorIdType">
        <option>PunchingCard</option>
        <option>BibNumber</option>
        <option>TimingTransponder</option>
        <option>Unknown</option>
      </select></label
    >
    <div class="field-pair">
      <label class="field"
        ><span>Control</span
        ><input v-model.number="values.control" type="number" required min="0" /></label
      ><label class="field"
        ><span>Control type</span
        ><select v-model="values.controlType">
          <option>Unknown</option>
          <option>Control</option>
          <option>Start</option>
          <option>Finish</option>
          <option>Check</option>
          <option>Clear</option>
        </select></label
      >
    </div>
    <label class="field check"
      ><span>Use current time</span><input v-model="useNow" type="checkbox"
    /></label>
    <label v-if="!useNow" class="field"
      ><span>Event time</span
      ><input v-model="values.time" type="datetime-local" step="0.001" required
    /></label>
    <label class="field"
      ><span>Competitor status</span
      ><select v-model="values.competitorStatus">
        <option>Unknown</option>
        <option>OK</option>
        <option>DNS</option>
        <option>DNF</option>
        <option>MP</option>
        <option>DSQ</option>
        <option>OverTime</option>
        <option>WaitingStart</option>
        <option>Running</option>
      </select></label
    >
    <div class="field-pair">
      <label class="field check"
        ><span>Cancellation</span><input v-model="values.cancellation" type="checkbox" /></label
      ><label class="field check"
        ><span>Net time</span><input v-model="values.netTime" type="checkbox"
      /></label>
    </div>
    <button type="submit" class="primary wide" :disabled="disabled || busy">
      {{ busy ? 'Sending…' : 'Send event' }}
    </button>
    <p v-if="message" role="status" :class="failed ? 'error-text' : 'success-text'">
      {{ message }}
    </p>
  </form>
</template>
