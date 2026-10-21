// Renders the generated HTML to PDF through puppeteer so we get a running
// footer (page x of y). Pure-CSS `@page` geometry stays authoritative:
// puppeteer's `margin` option is left unset, otherwise Chromium would place
// the header/footer box on top of the CSS page margin.
//
// usage: node render.mjs <input.html> <output.pdf> [footer-title]
import puppeteer from 'puppeteer';
import { existsSync, readdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

const [input, output, title = ''] = process.argv.slice(2);
if (!input || !output) {
  console.error('usage: node render.mjs <input.html> <output.pdf> [footer-title]');
  process.exit(2);
}

// Reuse the Chrome already on disk (fetched by mermaid-cli's puppeteer)
// instead of downloading another copy.
function findChrome() {
  if (process.env.PUPPETEER_EXECUTABLE_PATH) return process.env.PUPPETEER_EXECUTABLE_PATH;
  const root = join(homedir(), '.cache', 'puppeteer', 'chrome');
  if (!existsSync(root)) return undefined;
  for (const version of readdirSync(root).sort().reverse()) {
    for (const name of ['chrome-linux64/chrome', 'chrome-linux/chrome']) {
      const candidate = join(root, version, name);
      if (existsSync(candidate)) return candidate;
    }
  }
  return undefined;
}

const executablePath = findChrome();
const browser = await puppeteer.launch({
  ...(executablePath ? { executablePath } : {}),
  args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-gpu'],
});

const page = await browser.newPage();
await page.goto(`file://${input}`, { waitUntil: 'load' });

const escape = (s) => s.replace(/[<>&]/g, (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;' }[c]));

await page.pdf({
  path: output,
  printBackground: true,
  preferCSSPageSize: true,
  displayHeaderFooter: true,
  headerTemplate: '<div></div>',
  footerTemplate: `
    <div style="width:100%;font-size:8px;padding:0 18mm;color:#6E6E80;
                font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
      <span style="float:left">${escape(title)}</span>
      <span style="float:right"><span class="pageNumber"></span> / <span class="totalPages"></span></span>
    </div>`,
});

await browser.close();
console.log(`wrote ${output}`);
