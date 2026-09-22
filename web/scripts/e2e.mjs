import assert from 'node:assert/strict';
import { chromium } from 'playwright-core';
import { createServer } from 'vite';

const server = await createServer({ server: { host: '127.0.0.1', port: 0 }, logLevel: 'error' });
let browser;
try {
  await server.listen();
  const port = server.httpServer.address().port;
  browser = await chromium.launch({ channel: 'msedge', headless: true });
  const page = await browser.newPage();
  await page.goto('http://127.0.0.1:' + port, { waitUntil: 'domcontentloaded' });
  await page.locator('[data-diagram]').first().waitFor();
  try {
    await page.waitForFunction(() => document.querySelectorAll('[data-diagram="mermaid"] .diagram-result svg').length === 17, null, { timeout: 60000 });
  } catch (error) {
    const diagnostics = await page.locator('[data-diagram="mermaid"]').evaluateAll((blocks) =>
      blocks.map((block) => ({
        title: block.previousElementSibling?.textContent,
        rendered: Boolean(block.querySelector('.diagram-result svg')),
        error: block.querySelector('.diagram-error')?.textContent,
        loading: Boolean(block.querySelector('.diagram-loading')),
      })));
    console.log('Mermaid diagnostics:', JSON.stringify(diagnostics));
    throw error;
  }
  assert.equal(await page.locator('[data-diagram]').count(), 17);
  assert.equal(await page.locator('.code-block').count(), 0);
  assert.equal(await page.locator('[data-diagram="mermaid"] .diagram-error').count(), 0);
  const blockSvgMarkup = await page.locator('[data-diagram="mermaid"] .diagram-result svg').nth(16).innerHTML();
  assert.match(blockSvgMarkup, /MCU/);
  assert.match(blockSvgMarkup, /UART/);
  assert.match(blockSvgMarkup, /PC/);
  console.log('PASS: 저장된 Mermaid 종합 샘플 17개가 Edge에서 SVG로 렌더링됨');

  await page.locator('#theme-toggle').click();
  assert.equal(await page.locator('html.dark').count(), 1);
  await page.locator('#theme-toggle').click();
  await page.locator('#editor').fill('```mermaid\nsequenceDiagram\nA->>B: hi;there\nB->>A: ok\n```');
  await page.locator('.diagram-error').waitFor({ timeout: 20000 });
  assert.equal(await page.locator('body > div[id^="ddiagram-"]').count(), 0);
  assert.equal(await page.locator('svg[aria-roledescription="error"]').count(), 0);
  assert.match(await page.locator('.diagram-source').textContent(), /hi;there/);
  console.log('PASS: Mermaid 문법 오류는 메시지와 원문만 남기고 오류 SVG를 제거함');

  await page.locator('#editor').fill('# 새 제목\n\n```mermaid\nflowchart LR\nA-->B\n```');
  await page.waitForFunction(() => document.querySelectorAll('[data-diagram="mermaid"] .diagram-result svg').length === 1);
  assert.equal(await page.locator('[data-diagram]').count(), 1);
  assert.equal(await page.locator('body > div[id^="ddiagram-"]').count(), 0);
  console.log('PASS: 편집 후 미리보기가 갱신됨');
} finally {
  await browser?.close();
  await server.close();
}
