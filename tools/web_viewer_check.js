// Playwright pass over the web story viewer (docs/review/web-viewer-report.md).
// Needs `npm i playwright-core` somewhere on NODE_PATH and a Chromium from ~/.cache/ms-playwright
// (set CHROME to override). Usage: node tools/web_viewer_check.js <viewerBase> <workId> <outDir>
const { chromium } = require('playwright-core');
const fs = require('fs');
const [base, workId, out] = process.argv.slice(2);
const exe = process.env.CHROME || `${process.env.HOME}/.cache/ms-playwright/chromium-1248/chrome-linux64/chrome`;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function run(label, viewport, isMobile) {
  const browser = await chromium.launch({ executablePath: exe });
  const ctx = await browser.newContext({ viewport, deviceScaleFactor: 1, isMobile, hasTouch: isMobile });
  const page = await ctx.newPage();
  const consoleMsgs = [];
  const failed = [];
  page.on('console', (m) => consoleMsgs.push(`${m.type()}: ${m.text()}`));
  page.on('pageerror', (e) => consoleMsgs.push(`pageerror: ${e.message}`));
  page.on('requestfailed', (r) => failed.push(`${r.url()} ${r.failure()?.errorText}`));
  page.on('response', (r) => { if (r.status() >= 400) failed.push(`${r.status()} ${r.url()}`); });

  const t0 = Date.now();
  await page.goto(`${base}/s/${workId}`, { waitUntil: 'networkidle' });
  await sleep(4000);
  const loadMs = Date.now() - t0;
  const res = { label, viewport, loadMs, title: await page.title() };
  await page.screenshot({ path: `${out}/${label}-01-intro.png` });

  // Keyboard: Tab through focusable elements, record what gets focus.
  const focusSteps = [];
  for (let i = 0; i < 8; i++) {
    await page.keyboard.press('Tab');
    await sleep(250);
    focusSteps.push(await page.evaluate(() => {
      const a = document.activeElement;
      return a ? `${a.tagName.toLowerCase()}${a.getAttribute('aria-label') ? `[${a.getAttribute('aria-label')}]` : ''}${a.id ? '#' + a.id : ''}` : 'none';
    }));
    if (i === 1) await page.screenshot({ path: `${out}/${label}-02-tab-focus.png` });
  }
  res.focusSteps = focusSteps;
  // Click the photo area to give the Flutter view focus without activating anything, then arrows.
  await page.mouse.click(5, viewport.height / 2);
  await sleep(300);
  for (let i = 0; i < 2; i++) { await page.keyboard.press('ArrowRight'); await sleep(1300); }
  await page.screenshot({ path: `${out}/${label}-03-arrow-right-x2.png` });
  await page.keyboard.press('ArrowLeft');
  await sleep(1300);
  await page.screenshot({ path: `${out}/${label}-04-arrow-left.png` });
  // Enable the semantics tree (Flutter web exposes it on demand) and list roles/labels.
  await page.evaluate(() => {
    const p = document.querySelector('flt-semantics-placeholder');
    if (p) p.click();
  });
  await sleep(1500);
  res.semantics = await page.evaluate(() =>
    [...document.querySelectorAll('flt-semantics [role], flt-semantics[role]')]
      .map((e) => `${e.getAttribute('role')}: ${(e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 50)}`)
      .filter((s) => !s.endsWith(': '))
      .slice(0, 40));
  res.console = consoleMsgs;
  res.failed = failed;
  await browser.close();
  return res;
}

(async () => {
  fs.mkdirSync(out, { recursive: true });
  const results = [];
  results.push(await run('desktop', { width: 1280, height: 800 }, false));
  results.push(await run('phone', { width: 390, height: 844 }, true));
  console.log(JSON.stringify(results, null, 2));
})();
