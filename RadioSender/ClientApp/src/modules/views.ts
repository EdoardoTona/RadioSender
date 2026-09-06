import { defineAsyncComponent, type Component } from 'vue'

// Register optional operational views here. Configuration fields come from the C# descriptor.
export const moduleViews: Record<string, Component> = {
  'manual-input': defineAsyncComponent(() => import('../components/ManualInput.vue')),
}
