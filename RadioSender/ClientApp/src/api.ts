export async function api<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await fetch('/api/flow' + path, {
    method,
    headers: { 'Content-Type': 'application/json', 'X-RadioSender-Client': 'flow-editor' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await response.text()
  if (!response.ok) {
    let message = text || response.statusText
    try {
      message = JSON.parse(text).error ?? message
    } catch {
      /* Preserve a non-JSON server error. */
    }
    throw new Error(message)
  }
  return (text ? JSON.parse(text) : undefined) as T
}

type DesktopBridge = {
  sendMessage: (message: string) => void
  receiveMessage: (callback: (message: string) => void) => void
}
const bridge = (window as unknown as { external?: DesktopBridge }).external
export const isDesktop = typeof bridge?.sendMessage === 'function'
if (isDesktop) {
  bridge!.sendMessage(JSON.stringify({ kind: 'flow-editor-ready' }))
  window.addEventListener('pagehide', () =>
    bridge!.sendMessage(JSON.stringify({ kind: 'flow-editor-leaving' })),
  )
}
const pending = new Map<
  string,
  { resolve: (path: string | null) => void; reject: (error: Error) => void }
>()
if (isDesktop)
  bridge!.receiveMessage((message) => {
    try {
      const result = JSON.parse(message)
      if (result.kind === 'file-dialog-result') {
        const promise = pending.get(result.id)
        pending.delete(result.id)
        if (result.error) promise?.reject(new Error(result.error))
        else promise?.resolve(result.path ?? null)
      }
      if (result.kind === 'closing') window.dispatchEvent(new Event('desktop-closing'))
    } catch {
      /* Ignore messages belonging to other desktop features. */
    }
  })
export function chooseFile(mode: 'open' | 'save', path: string): Promise<string | null> {
  if (!isDesktop) return Promise.resolve(null)
  const id = crypto.randomUUID()
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject })
    bridge!.sendMessage(JSON.stringify({ kind: 'file-dialog', id, mode, path }))
  })
}
export function finishDesktopClose() {
  bridge?.sendMessage?.(JSON.stringify({ kind: 'close-ready' }))
}
