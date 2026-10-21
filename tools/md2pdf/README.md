# md2pdf — markdown to print-quality PDF

One small CLI that turns a markdown file into a typographically correct PDF:
CommonMark (plus tables and typographer), vector diagrams, page-break control,
and a running footer with page numbers.

```bash
node tools/md2pdf/md2pdf.mjs docs/my-doc.md
node tools/md2pdf/md2pdf.mjs docs/my-doc.md --eyebrow "Architecture Note" --title "My Doc"
node tools/md2pdf/md2pdf.mjs docs/my-doc.md -o /tmp/out.pdf --no-mermaid
node tools/md2pdf/md2pdf.mjs docs/my-doc.md --dump-html -o /tmp/debug.html   # inspect the HTML
```

Install once (from this directory):

```bash
bun install        # or: npm install
```

## Design: own as little as possible

The tool deliberately delegates the hard parts to maintained software and owns
only the parts that are actually opinion:

| concern | owner |
| --- | --- |
| markdown → HTML | [markdown-it](https://github.com/markdown-it/markdown-it) (CommonMark + tables) |
| HTML → PDF, pagination, vector | Chromium (via puppeteer) |
| mermaid → SVG | `@mermaid-js/mermaid-cli` |
| **typography & print rules** | **`style.css` — ours, and the whole point** |

So there is almost no code here: `md2pdf.mjs` (CLI + a small markdown-it plugin
for mermaid fences and heading binding) and `render.mjs` (the puppeteer page).
Everything that makes the PDF look good lives in `style.css`.

| file | role |
| --- | --- |
| `md2pdf.mjs` | CLI: parse args, render mermaid, bind headings, drive the pipeline |
| `style.css` | the print stylesheet — all typography lives here |
| `render.mjs` | puppeteer print step; supplies the running footer |
| `mermaid.json` | mermaid theme (`base` + palette), `htmlLabels: false` |

Requirements: Node, Chromium (found automatically, or `PUPPETEER_EXECUTABLE_PATH`),
and `bunx` (or `npx`) for the mermaid step. SVG output is cached under
`~/.cache/md2pdf`.

## Print best practices this tool follows

**Units.** Print uses absolute units: `pt` for type, `mm` for page geometry.
Pixels are screen-relative and mean nothing on paper. `@page` owns the page box;
the bottom margin is the largest so the running footer has room.

**Pagination.** Modern `break-*` and legacy `page-break-*` are always written in
pairs — Chromium treats them differently and both are needed. The three rules
that prevent the visible defects:

- `break-after: avoid` on headings — keeps a heading off the page foot.
- `break-inside: avoid` on `figure`/`pre`/`blockquote`/`table`/`li` — never slice
  a block in half.
- `orphans: 3; widows: 3` on paragraphs — never strand a single line.

`break-after: avoid` alone still strands headings when the *first lines* of the
following paragraph cannot fit on the page, so the plugin additionally wraps each
heading with the block that follows it in `.keep { break-inside: avoid }`. Write
`<!-- pagebreak -->` in the markdown to force a hard break.

**Typography.** 11 pt body, `text-wrap: balance` on headings, `text-wrap: pretty`
on body, `hyphens: auto`, and `font-synthesis: none` so the browser never draws a
fake bold/italic (which reads badly on paper). Tracked uppercase is used for small
labels only — lowercase body text is never letter-spaced.

**Justification.** Justify only when the measure is long *and* hyphenation is on.
This tool defaults to ragged-right, because technical prose full of inline code
spans produces rivers under justification. Switch to `text-align: justify` for
prose-only documents with a 50+ character measure.

**Colour.** Ink colours are near-black, the accent is a single green used only for
rules and labels; `print-color-adjust: exact` keeps the few backgrounds that carry
meaning. Body text is never a tinted "rich black".

**Vector diagrams.** Mermaid is rendered to SVG and inlined. Two fixes matter:

1. `htmlLabels: false` — HTML labels use `<foreignObject>`, which Chromium
   rasterises when printing.
2. Strip `filter: drop-shadow(...)` — a CSS filter also forces rasterisation.

Rasterised diagrams are the classic cause of "blurry when I zoom in". Verify a
result with `pdfimages -list out.pdf`: the image list should be empty.

**Footer.** Page geometry stays in CSS and puppeteer's `margin` option is left
unset — otherwise the footer box lands on top of the `@page` margin. The footer
template renders the document title on the left and `page / pages` on the right.

## Extending

- **Theme / palette**: edit the `:root` variables in `style.css`.
- **New markdown feature**: it is markdown-it, so add a plugin rather than a
  parser branch.
- **A different PDF engine**: replace `render.mjs`; `md2pdf.mjs` only needs a
  function that writes a PDF from an HTML file.

## Limits

- `md2pdf.mjs` falls back to `chromium --headless --print-to-pdf` (no page
  numbers) and to source code blocks (no diagram) if the node tools are absent,
  so it always produces a valid PDF.
- Mermaid rendering needs `bunx`/`npx` and downloads mermaid-cli on first use.
