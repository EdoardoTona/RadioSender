import { test, expect } from '@playwright/test'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
const headers = { 'X-RadioSender-Client': 'flow-editor' }

test('create, autosave, connect, inspect, replay and edit a running flow', async ({
  page,
  request,
}, testInfo) => {
  const directory = await mkdtemp(join(tmpdir(), 'radiosender-editor-'))
  const documentPath = join(directory, 'race.radiosender.json')
  const pageErrors: string[] = []
  page.on('pageerror', (e) => pageErrors.push(e.message))
  const graphNode = (name: string) =>
    page
      .locator('.vue-flow__node')
      .filter({ has: page.locator('strong', { hasText: new RegExp(`^${name}$`) }) })
  const connect = async (from: string, to: string) => {
    const before = await page.locator('.vue-flow__edge').count()
    const source = await graphNode(from).locator('.vue-flow__handle.source').boundingBox()
    const target = await graphNode(to).locator('.vue-flow__handle.target').boundingBox()
    expect(source).not.toBeNull()
    expect(target).not.toBeNull()
    await page.mouse.move(source!.x + source!.width / 2, source!.y + source!.height / 2)
    await page.mouse.down()
    await page.mouse.move(target!.x + target!.width / 2, target!.y + target!.height / 2, {
      steps: 20,
    })
    await page.mouse.up()
    await expect(page.locator('.vue-flow__edge')).toHaveCount(before + 1)
  }
  try {
    await page.goto('/flows/index.html')
    await expect(page.getByRole('heading', { name: 'Open or create a new flow' })).toBeVisible()
    await expect(page.locator('.library')).toHaveCount(0)
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await page.getByLabel('Full JSON file path').fill(documentPath)
    await page.getByRole('button', { name: 'Save file', exact: true }).click()
    await expect(page.locator('.document-title')).toContainText('Editor')
    await page.locator('.library-module').filter({ hasText: 'Manual input' }).click()
    await expect(page.getByLabel('Competitor ID', { exact: true })).toHaveCount(0)
    await page.locator('.library-module').filter({ hasText: 'Passthrough' }).click()
    await page.getByLabel('Name', { exact: true }).fill('After mapping')
    await page.locator('.library-module').filter({ hasText: 'File output' }).click()
    await page.getByLabel('Name', { exact: true }).fill('Mapped archive')
    await page.getByLabel('File path').fill('mapped.csv')
    await page.locator('.library-module').filter({ hasText: 'File output' }).click()
    await page.getByLabel('Name', { exact: true }).fill('Raw archive')
    await page.getByLabel('File path').fill('raw.csv')
    await page.getByTitle('Fit graph', { exact: true }).click()
    await connect('Manual input', 'After mapping')
    await page.getByRole('button', { name: 'Create filter', exact: true }).click()
    await page.getByLabel('Filter name', { exact: true }).fill('Finish mapping')
    await page.getByRole('button', { name: 'Create', exact: true }).click()
    await page.getByLabel('Control mapping from', { exact: true }).fill('35')
    await page.getByLabel('Control mapping to', { exact: true }).fill('1')
    await page.getByRole('button', { name: 'Add control mapping', exact: true }).click()
    await page.getByLabel('Delay (ms)', { exact: true }).fill('150')
    await expect(page.locator('.vue-flow__edge-text')).toHaveText('Finish mapping · 150 ms')
    await connect('After mapping', 'Mapped archive')
    await connect('Manual input', 'Raw archive')
    await expect
      .poll(async () => JSON.parse(await readFile(documentPath, 'utf8')).edges.length)
      .toBe(3)
    const saved = JSON.parse(await readFile(documentPath, 'utf8'))
    expect(saved.filters[0].name).toBe('Finish mapping')
    expect(saved.edges[0].filterId).toBe(saved.filters[0].id)
    await page.screenshot({ path: testInfo.outputPath('editor.png'), fullPage: true })
    await page.getByRole('button', { name: 'Apply', exact: true }).click()
    await page.getByRole('button', { name: 'Apply configuration', exact: true }).click()
    await expect(page.locator('.runtime-strip')).toContainText('Running:')
    await expect(page.locator('.library')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Edit', exact: true })).toBeVisible()
    await graphNode('Manual input').click()
    await page.getByLabel('Competitor ID', { exact: true }).fill('123')
    await page.getByRole('button', { name: 'Send event', exact: true }).click()
    await expect(page.locator('.event-table tbody')).toContainText('123')
    await expect
      .poll(async () => await readFile(join(directory, 'mapped.csv'), 'utf8'))
      .toContain('123;1;')
    await expect
      .poll(async () => await readFile(join(directory, 'raw.csv'), 'utf8'))
      .toContain('123;35;')
    // Several live snapshots must preserve the same DOM row and its selection.
    const selection = page.locator('.event-table tbody input[type=checkbox]')
    await selection.check()
    await page
      .locator('.event-table tbody tr')
      .evaluate((el) => el.setAttribute('data-stability-marker', 'original'))
    await page.waitForTimeout(1600)
    await expect(
      page.locator('.event-table tbody tr[data-stability-marker="original"]'),
    ).toHaveCount(1)
    await expect(selection).toBeChecked()
    await graphNode('After mapping').click()
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
    await expect(
      page.getByRole('button', { name: 'Send selected events', exact: true }),
    ).toBeHidden()
    await page.screenshot({ path: testInfo.outputPath('flow.png'), fullPage: true })
    await page.getByRole('tab', { name: 'Node logs', exact: true }).click()
    await expect(page.locator('.log-table')).toContainText('Output replay')
    await page.locator('.app-header nav').getByRole('button', { name: 'Logs', exact: true }).click()
    await expect(page.locator('.log-table')).toContainText('Applied flow')
    await expect(page.locator('.log-table')).not.toContainText('Output replay')
    await page.getByLabel('Include node logs').check()
    await expect(page.locator('.log-table')).toContainText('Output replay')
    await page.locator('.app-header nav').getByRole('button', { name: 'Flow', exact: true }).click()
    await page.getByRole('button', { name: 'Edit', exact: true }).click()
    await graphNode('Mapped archive').click()
    await page.getByLabel('File path').fill('updated.csv')
    await page.getByRole('button', { name: 'Apply', exact: true }).click()
    await page.getByRole('button', { name: 'Apply configuration', exact: true }).click()
    await graphNode('Manual input').click()
    await page.getByLabel('Competitor ID', { exact: true }).fill('456')
    await page.getByRole('button', { name: 'Send event', exact: true }).click()
    await expect
      .poll(async () => await readFile(join(directory, 'updated.csv'), 'utf8'))
      .toContain('456;1;')
    await page.getByRole('button', { name: 'Stop', exact: true }).click()
    await expect(page.locator('.runtime-strip')).toContainText('Flow stopped')
    await page.reload()
    await expect(page.getByRole('heading', { name: 'Open or create a new flow' })).toBeVisible()
    await page.getByRole('button', { name: 'Open', exact: true }).click()
    await page.getByLabel('Full JSON file path').fill(documentPath)
    await page.getByRole('button', { name: 'Open file', exact: true }).click()
    await expect(page.locator('.module-node')).toHaveCount(4)
    await expect(page.locator('.library')).toHaveCount(0)
    await expect(page.locator('.runtime-strip')).toContainText('Flow stopped')
    expect(pageErrors).toEqual([])
  } finally {
    await request.post('/api/flow/runtime/stop', { headers, data: {} })
    await rm(directory, { recursive: true, force: true })
  }
})

test('autosave conflict keeps edits and Save As recovers them', async ({ page }) => {
  const directory = await mkdtemp(join(tmpdir(), 'radiosender-conflict-'))
  const path = join(directory, 'original.json'),
    recovered = join(directory, 'recovered.json')
  try {
    await page.goto('/flows/index.html')
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await page.getByLabel('Full JSON file path').fill(path)
    await page.getByRole('button', { name: 'Save file', exact: true }).click()
    await page.locator('.library-module').filter({ hasText: 'Manual input' }).click()
    await expect.poll(async () => JSON.parse(await readFile(path, 'utf8')).nodes.length).toBe(1)
    const original = await readFile(path, 'utf8')
    await writeFile(path, original + '\n')
    await page.getByLabel('Name', { exact: true }).fill('Recovered manual')
    await expect(page.locator('.app-alert')).toContainText('changed outside')
    await expect(page.getByLabel('Name', { exact: true })).toHaveValue('Recovered manual')
    await page.getByRole('button', { name: 'Save As', exact: true }).click()
    await page.getByLabel('Full JSON file path').fill(recovered)
    await page.getByRole('button', { name: 'Save file', exact: true }).click()
    await expect(page.locator('.document-title')).toContainText('recovered.json')
    await expect
      .poll(async () => JSON.parse(await readFile(recovered, 'utf8')).nodes[0].name)
      .toBe('Recovered manual')
    expect(await readFile(path, 'utf8')).toBe(original + '\n')
    await expect(page.locator('.app-alert')).toHaveCount(0)
  } finally {
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
