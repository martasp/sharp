#!/usr/bin/env node
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const benchDir = join(root, 'benchmarks');
const outFile = join(benchDir, 'TRAJECTORY.md');

function parseTime(text, pattern) {
  const match = pattern.exec(text);
  if (!match) return null;
  const value = /([\d.]+)\s*(ms|s)/.exec(match[1]);
  if (!value) return null;
  return Number(value[1]) / (value[2] === 'ms' ? 1000 : 1);
}

function parseNumber(text, pattern) {
  const match = pattern.exec(text);
  return match ? Number(match[1]) : null;
}

function median(values) {
  const sorted = [...values].sort((a, b) => a - b);
  if (sorted.length === 0) return null;
  const mid = sorted.length >> 1;
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

const files = readdirSync(benchDir).filter((name) =>
  /^benchmark-(jit|published)-\d{4}-\d{2}-\d{2}-\d{6}\.txt$/.test(name),
);

const runs = [];
for (const name of files) {
  const match = /^benchmark-(jit|published)-(\d{4}-\d{2}-\d{2})-\d{6}\.txt$/.exec(name);
  if (!match) continue;
  const text = readFileSync(join(benchDir, name), 'utf8');
  runs.push({
    kind: match[1],
    date: match[2],
    ours: parseTime(text, /run1\s+wall\s+([\d.]+\s*(?:ms|s))/),
    cold: parseTime(text, /dotnet build\s+cold\s+wall\s+([\d.]+\s*(?:ms|s))/),
    speedup: parseNumber(text, /speed-up vs dotnet build cold:\s*([\d.]+)x/),
  });
}

const kinds = [
  ['jit', 'In-process JIT'],
  ['published', 'Published AOT'],
];

const seriesDefs = [
  { key: 'ours', title: 'Ours', label: 'ours (run1)', char: '*' },
  { key: 'cold', title: 'dotnet build (cold)', label: 'dotnet build (cold)', char: '+' },
];

function daily(kind) {
  const byDate = new Map();
  for (const run of runs) {
    if (run.kind !== kind) continue;
    if (!byDate.has(run.date)) byDate.set(run.date, { ours: [], cold: [], speedup: [] });
    const bucket = byDate.get(run.date);
    if (run.ours != null) bucket.ours.push(run.ours);
    if (run.cold != null) bucket.cold.push(run.cold);
    if (run.speedup != null) bucket.speedup.push(run.speedup);
  }
  return [...byDate.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([date, bucket]) => ({
      date,
      ours: median(bucket.ours),
      cold: median(bucket.cold),
      speedup: median(bucket.speedup),
    }));
}

function niceStep(value) {
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const normalized = value / magnitude;
  const candidate = [1, 2, 2.5, 5, 10].find((item) => normalized <= item) ?? 10;
  return candidate * magnitude;
}

function renderChart(title, rows, series) {
  const dates = rows.map((row) => row.date);
  const cell = 2;
  const width = dates.length * cell;
  const values = series.flatMap((item) => item.values).filter((value) => value != null);
  if (values.length === 0) return `${title}\n(no data)\n`;

  const height = 10;
  const step = niceStep(Math.max(...values) / (height - 1));
  const yMax = step * (height - 1);
  const grid = Array.from({ length: height }, () => Array(width).fill(' '));
  for (const item of series) {
    item.values.forEach((value, index) => {
      if (value == null) return;
      const x = index * cell;
      const row = Math.min(height - 1, Math.max(0, Math.round((value / yMax) * (height - 1))));
      const y = height - 1 - row;
      grid[y][x] = grid[y][x] === ' ' || grid[y][x] === item.char ? item.char : '#';
    });
  }

  const labelWidth = 5;
  const lines = [title];
  for (let row = 0; row < height; row++) {
    const value = yMax - row * step;
    const text = step < 1 ? value.toFixed(1) : value.toFixed(0);
    lines.push(`${text.padStart(labelWidth - 1)}s|${grid[row].join('').replace(/\s+$/, '')}`);
  }
  lines.push(`${' '.repeat(labelWidth - 1)} +${'-'.repeat(width)}`);

  const labelCount = Math.max(1, Math.min(6, Math.round(width / 10)));
  const maxStart = Math.max(0, width - 5);
  const axis = Array(width).fill(' ');
  for (let i = 0; i < labelCount; i++) {
    const column = labelCount === 1 ? 0 : Math.round((i * maxStart) / (labelCount - 1));
    const dateIndex = Math.min(dates.length - 1, Math.round(column / cell));
    const label = dates[dateIndex].slice(5);
    for (let j = 0; j < label.length && column + j < width; j++) axis[column + j] = label[j];
  }
  lines.push(`${' '.repeat(labelWidth)}${axis.join('')}`);
  lines.push(`${' '.repeat(labelWidth)}${series.map((item) => `${item.char} ${item.label}`).join('   ')}`);
  return `${lines.join('\n')}\n`;
}

function formatSeconds(value) {
  return value == null ? '—' : value.toFixed(2);
}

const sections = kinds.map(([kind, title]) => {
  const rows = daily(kind);
  const count = runs.filter((run) => run.kind === kind).length;
  const first = rows[0];
  const last = rows.at(-1);
  const charts = seriesDefs
    .map((def) => {
      const series = [{ ...def, values: rows.map((row) => row[def.key]) }];
      return `### ${def.title}\n\n\`\`\`text\n${renderChart(`${title} — ${def.title} (seconds)`, rows, series)}\`\`\`\n`;
    })
    .join('\n');

  const table = [
    '| Date | Ours (s) | dotnet cold (s) | Speed-up |',
    '|---|---:|---:|---:|',
    ...rows.map(
      (row) =>
        `| ${row.date} | ${formatSeconds(row.ours)} | ${formatSeconds(row.cold)} | ${
          row.speedup == null ? '—' : `${row.speedup.toFixed(2)}x`
        } |`,
    ),
  ].join('\n');

  return `## ${title}

${count} runs on ${rows.length} days (${first.date} to ${last.date}); each point is the daily median, all times in seconds.

${charts}
${table}
`;
});

const markdown = `# Benchmark trajectory

Timings parsed from the committed captures in \`benchmarks/\`.

Regenerate with:

\`\`\`bash
node tools/benchmark-graph.mjs --write
\`\`\`

${sections.join('\n')}`;

if (process.argv.includes('--write')) {
  writeFileSync(outFile, markdown);
  console.log(`wrote ${relative(root, outFile)}`);
} else {
  process.stdout.write(markdown);
}
