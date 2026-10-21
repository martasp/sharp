#!/usr/bin/env node
// md2pdf: markdown -> print-quality PDF.
//
// Design: own as little as possible. markdown-it (CommonMark + tables) turns the
// source into HTML; the stylesheet owns every typographic decision; Chromium
// owns pagination and vector rendering. This file only wires those together,
// plus the two things markdown-it will not do for us: render ```mermaid fences
// to inline SVG, and keep headings from being stranded at the foot of a page.
//
// usage:
//   node md2pdf.mjs docs/foo.md                 # -> docs/foo.pdf
//   node md2pdf.mjs docs/foo.md -o /tmp/x.pdf --eyebrow "API Reference"
//   node md2pdf.mjs docs/foo.md --no-mermaid    # print fences as code blocks

import MarkdownIt from "markdown-it";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const CACHE = join(homedir(), ".cache", "md2pdf", "svg");

// ------------------------------------------------------------------ mermaid
// Render one mermaid diagram to a self-contained inline <svg>. Results are
// cached by source hash because mmdc spins up a browser per diagram (~seconds).
function renderMermaid(source, index) {
  mkdirSync(CACHE, { recursive: true });
  const digest = createHash("sha1").update(source).digest("hex").slice(0, 16);
  const cached = join(CACHE, `${digest}.svg`);

  if (!existsSync(cached)) {
    const tmp = mkdtempSync(join(tmpdir(), "md2pdf-"));
    writeFileSync(join(tmp, "in.mmd"), source);
    writeFileSync(join(tmp, "puppeteer.json"),
      JSON.stringify({ args: ["--no-sandbox", "--disable-setuid-sandbox"] }));

    const run = spawnSync("bunx", ["--bun", "@mermaid-js/mermaid-cli",
      "-i", join(tmp, "in.mmd"), "-o", join(tmp, "out.svg"),
      "-c", join(HERE, "mermaid.json"), "-p", join(tmp, "puppeteer.json"), "-b", "white"],
      { encoding: "utf8" });

    if (run.status !== 0) throw new Error((run.stderr || run.stdout || "mermaid-cli failed").trim());
    writeFileSync(cached, readFileSync(join(tmp, "out.svg"), "utf8"));
  }

  let svg = readFileSync(cached, "utf8");
  // Unique ids per diagram (mermaid hard-codes "my-svg"), and strip the CSS
  // drop-shadow filters: a filtered shape is rasterised by Chromium when
  // printing, which is exactly what makes a zoomed-in diagram blurry.
  svg = svg.replaceAll("my-svg", `md2svg${index}`);
  svg = svg.replace(/filter:drop-shadow\(.*?\)\)/g, "filter:none");
  svg = svg.replace(/<filter\b[^>]*>.*?<\/filter>/gs, "");
  svg = svg.replace(/style="max-width:[^"]*"/, 'style="background:#ffffff"');
  return svg;
}

// --------------------------------------------------------------------- parse
// A tiny markdown-it plugin: mermaid fences -> <figure>, and heading binding.
function md2pdfPlugin(useMermaid) {
  let diagram = 0;

  return (md) => {
    // ```mermaid -> inline SVG figure (or a plain code block with --no-mermaid).
    const fence = md.renderer.rules.fence;
    md.renderer.rules.fence = (tokens, idx, options, env, self) => {
      const token = tokens[idx];
      if (useMermaid && token.info.trim().toLowerCase() === "mermaid") {
        try {
          return `<figure>${renderMermaid(token.content, ++diagram)}</figure>\n`;
        } catch (error) {
          process.stderr.write(`warning: mermaid render failed (${error.message}); emitting source\n`);
        }
      }
      return fence(tokens, idx, options, env, self);
    };

    // Bind each heading to the block that follows it. Chromium's
    // `break-after: avoid` alone still strands a heading when the following
    // paragraph's first lines do not fit, so we make the pair one unbreakable box.
    //
    // A top-level block starts at level 0 and runs up to (not including) the next
    // level-0 token. We wrap [heading .. end of next block] in a .keep div, using
    // html_block tokens in place of raw divs so the markup stays balanced.
    md.core.ruler.push("bind-headings", (state) => {
      const tokens = state.tokens;
      const out = [];
      const html = (content) => {
        const token = new state.Token("html_block", "", 0);
        token.content = content;
        token.map = [0, 0];
        return token;
      };

      for (let i = 0; i < tokens.length; i++) {
        const token = tokens[i];
        if (token.type !== "heading_open") { out.push(token); continue; }

        // heading = heading_open + inline + heading_close
        const heading = [token, tokens[i + 1], tokens[i + 2]];
        i += 2;

        // find the next block: scan forward to the token that closes the opening
        // token of that block. Self-closing blocks (hr, fence, html_block) have
        // nesting 0; container blocks (paragraph, list, blockquote) end at -1.
        const blockStart = i + 1;
        if (blockStart >= tokens.length || tokens[blockStart].level !== 0) {
          out.push(...heading);   // nothing to bind (e.g. trailing heading)
          continue;
        }
        let blockEnd = blockStart + 1;
        if (tokens[blockStart].nesting === 1) {
          let depth = 1;
          while (blockEnd < tokens.length && depth > 0) {
            depth += tokens[blockEnd].nesting;
            blockEnd++;
          }
        }

        out.push(html('<div class="keep">'), ...heading, ...tokens.slice(blockStart, blockEnd), html("</div>"));
        i = blockEnd - 1;
      }
      state.tokens = out;
    });
  };
}

// ------------------------------------------------------------------- assemble
function buildHtml(html, { title, eyebrow }) {
  const head = eyebrow
    ? `<header class="title"><p class="eyebrow">${escapeHtml(eyebrow)}</p>${html}</header><hr class="rule"/>`
    : html;
  return `<!doctype html><html lang="en"><head><meta charset="utf-8">` +
    `<title>${escapeHtml(title)}</title><style>${readFileSync(join(HERE, "style.css"), "utf8")}</style>` +
    `</head><body>\n${head}\n</body></html>`;
}

const escapeHtml = (s) => s.replace(/[<>&]/g, (c) => ({ "<": "&lt;", ">": "&gt;", "&": "&amp;" }[c]));

// --------------------------------------------------------------------- cli
function parseArgs(argv) {
  const args = { input: null, output: null, title: null, eyebrow: null, mermaid: true };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "-o" || a === "--output") args.output = argv[++i];
    else if (a === "--title") args.title = argv[++i];
    else if (a === "--eyebrow") args.eyebrow = argv[++i];
    else if (a === "--no-mermaid") args.mermaid = false;
    else if (a === "--dump-html") args.dumpHtml = true;
    else if (a === "-h" || a === "--help") args.help = true;
    else if (!a.startsWith("-")) args.input = a;
  }
  return args;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help || !args.input) {
    console.log("usage: md2pdf <input.md> [-o out.pdf] [--title T] [--eyebrow E] [--no-mermaid] [--dump-html]");
    process.exit(args.help ? 0 : 2);
  }

  const md = new MarkdownIt({ html: true, linkify: true, typographer: true }).use(md2pdfPlugin(args.mermaid));
  const body = md.render(readFileSync(args.input, "utf8"));

  if (args.dumpHtml) {
    const out = args.output || join(tmpdir(), "md2pdf.html");
    writeFileSync(out, buildHtml(body, { title: args.title || basename(args.input), eyebrow: args.eyebrow }));
    console.log(out);
    return;
  }

  // The first H1 becomes the document title unless one is given.
  const firstH1 = body.match(/<h1[^>]*>(.*?)<\/h1>/s);
  const title = args.title || (firstH1 ? firstH1[1].replace(/<[^>]+>/g, "").trim() : "") ||
    basename(args.input);

  const output = args.output || args.input.replace(/\.md$/i, ".pdf");
  const tmp = mkdtempSync(join(tmpdir(), "md2pdf-"));
  const htmlPath = join(tmp, "document.html");
  writeFileSync(htmlPath, buildHtml(body, { title, eyebrow: args.eyebrow }));

  const engine = renderPdf(htmlPath, output, title);
  console.log(`${args.input} -> ${output} (${engine})`);
}

const basename = (p) => p.split(/[\\/]/).pop().replace(/\.[^.]+$/, "");

// --------------------------------------------------------------------- render
// puppeteer gives the running footer with page numbers; chromium CLI is the
// fallback when node packages are unavailable (no page numbers).
function renderPdf(htmlPath, output, title) {
  const run = spawnSync(process.execPath, [
    join(HERE, "render.mjs"), htmlPath, resolve(output), title], { encoding: "utf8" });
  if (run.status === 0) return "puppeteer";

  process.stderr.write(`note: puppeteer render failed (${(run.stderr || "").trim().split("\n").pop()}); falling back to chromium\n`);
  const chromium = ["chromium", "chromium-browser", "google-chrome"].find(which);
  if (!chromium) throw new Error("neither puppeteer nor chromium is available");
  const cli = spawnSync(chromium, ["--headless", "--no-sandbox", "--disable-gpu",
    "--no-pdf-header-footer", `--print-to-pdf=${resolve(output)}`, `file://${htmlPath}`], { encoding: "utf8" });
  if (cli.status !== 0) throw new Error((cli.stderr || "chromium failed").trim());
  return "chromium";
}

function which(cmd) {
  return spawnSync("sh", ["-c", `command -v ${cmd}`]).status === 0;
}

main();
