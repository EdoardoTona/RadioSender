import { test, expect } from '@playwright/test'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

test('create, autosave, branch, inspect and replay a flow through the UI', async ({
  page,
  request,
}, testInfo) => {
  const directory = await mkdtemp(join(tmpdir(), 'radiosender-editor-'))
  const documentPath = join(directory, 'race.radiosender.json')
  const pageErrors: string[] = []
  page.on('pageerror', (e) => pageErrors.push(e.message))
  try {
    await page.goto('/flows/index.html')
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await page.getByLabel('Full JSON file path').fill(documentPath)
    await page.getByRole('button', { name: 'Save file', exact: true }).click()
    await expect(page.locator('.document-title')).toContainText('race.radiosender.json')
    await page.locator('.library-module').filter({ hasText: 'Manual input' }).click()
    await page.locator('.library-module').filter({ hasText: 'Passthrough' }).click()
    await page.getByLabel('Name', { exact: true }).fill('After mapping')
    await page.locator('.library-module').filter({ hasText: 'File output' }).click()
    await page.getByLabel('Name', { exact: true }).fill('Mapped archive')
    await page.getByLabel('File path').fill('mapped.csv')
    await page.locator('.library-module').filter({ hasText: 'File output' }).click()
    await page.getByLabel('Name', { exact: true }).fill('Raw archive')
    await page.getByLabel('File path').fill('raw.csv')

    await page.getByRole('button', { name: 'Connection list', exact: true }).click()
    const connect = async (from: string, to: string) => {
      await page.getByLabel('Connection source').selectOption({ label: from })
      await page.getByLabel('Connection target').selectOption({ label: to })
      await page.getByRole('button', { name: 'Connect', exact: true }).click()
    }
    await connect('Manual input', 'After mapping')
    await page.getByRole('button', { name: 'Add filter & mapping', exact: true }).click()
    await page.getByLabel('Control mapping from', { exact: true }).fill('35')
    await page.getByLabel('Control mapping to', { exact: true }).fill('1')
    await page.getByRole('button', { name: 'Add control mapping', exact: true }).click()
    await connect('After mapping', 'Mapped archive')
    await connect('Manual input', 'Raw archive')
    await expect(page.locator('.save-state')).toHaveText('Saved')
    await expect
      .poll(async () => JSON.parse(await readFile(documentPath, 'utf8')).edges.length)
      .toBe(3)
    await page.getByRole('button', { name: 'Apply & start', exact: true }).click()
    await page.getByRole('button', { name: 'Apply configuration', exact: true }).click()
    await expect(page.locator('.runtime-strip')).toContainText('Running:')
    await page.getByRole('button', { name: 'Hide connection list', exact: true }).click()
    await page
      .locator('.module-node')
      .filter({ has: page.locator('strong', { hasText: /^Manual input$/ }) })
      .click()
    await page.getByLabel('Competitor ID', { exact: true }).fill('123')
    await page.getByRole('button', { name: 'Send event', exact: true }).click()
    await expect(page.locator('.event-table tbody')).toContainText('123')
    await expect
      .poll(async () => await readFile(join(directory, 'mapped.csv'), 'utf8'))
      .toContain('123;1;')
    await expect
      .poll(async () => await readFile(join(directory, 'raw.csv'), 'utf8'))
      .toContain('123;35;')

    await page
      .locator('.module-node')
      .filter({ has: page.locator('strong', { hasText: /^After mapping$/ }) })
      .click()
    await page.getByRole('button', { name: 'Output', exact: true }).click()
    await expect(page.locator('.event-table tbody tr')).toHaveCount(1)
    await page.locator('.event-table tbody input[type=checkbox]').check()
    await page.getByRole('button', { name: 'Replay output (1)', exact: true }).click()
    await page.getByRole('button', { name: 'Send selected events', exact: true }).click()
    await expect
      .poll(
        async () =>
          (await readFile(join(directory, 'mapped.csv'), 'utf8')).trim().split('\n').length,
      )
      .toBe(2)
    expect((await readFile(join(directory, 'raw.csv'), 'utf8')).trim().split('\n')).toHaveLength(1)

    await page.screenshot({ path: testInfo.outputPath('flow-editor.png'), fullPage: true })
    await page.getByRole('button', { name: 'Stop', exact: true }).click()
    await expect(page.locator('.runtime-strip')).toContainText('Flow stopped')
    await page.reload()
    await expect(page.locator('.document-title')).toContainText('race.radiosender.json')
    await expect(page.locator('.module-node')).toHaveCount(4)
    await expect(page.locator('.runtime-strip')).toContainText('Flow stopped')
    expect(pageErrors).toEqual([])
  } finally {
    await request.post('/api/flow/runtime/stop', {
      headers: { 'X-RadioSender-Client': 'flow-editor' },
      data: {},
    })
    await rm(directory, { recursive: true, force: true })
  }
})

test('mutating APIs reject cross-origin and headerless requests', async ({ request }) => {
  const withoutHeader = await request.post('/api/flow/runtime/stop', { data: {} })
  expect(withoutHeader.status()).toBe(403)
  const foreignOrigin = await request.get('/api/flow/modules', {
    headers: { Origin: 'https://example.org' },
  })
  expect(foreignOrigin.status()).toBe(403)
})
