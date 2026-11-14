using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sharp.Tests;

public static class CliComparisonHelper
{
    // Token used in place of the repo root so snapshots stay machine-independent. Must be applied
    // BEFORE rendering: Verify would otherwise swap the real path for {SolutionDirectory} after
    // the side-by-side was laid out, shortening only the affected lines and misaligning the pipes.
    // Normalizing first keeps wrap points and column widths consistent with the saved text.
    public const string SolutionToken = "{SolutionDirectory}";

    // Replaces the repo root with the token in arbitrary text. Exposed separately from Normalize
    // because the column layout depends on the *rendered* length of every line, so any text that
    // reaches GenerateSideBySide - body output and titles alike - must be culled first. A title
    // still carrying the real path would size the columns from this machine's checkout depth and
    // leave the saved snapshot unmatchable anywhere else.
    public static string CullRepoPath(string text) =>
        text
            .Replace('\\', '/')
            .Replace(GetRepoRootPrefixed(), SolutionToken + '/');

    private static string GetRepoRootPrefixed() =>
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."))
            .Replace('\\', '/') + '/';

    // Shared normalization for any our-vs-dotnet comparison, so behavior parity is asserted on
    // identical text regardless of which command produced it. Wall-clock and restore banners add
    // noise per run; drop them so the comparison is deterministic.
    public static string Normalize(string raw)
    {
        string normalized = StripTerminalFrames(raw)
            .Replace("\r\n", "\n")
            // A pty stream may leave stray CRs that weren't part of a CRLF pair.
            .Replace('\r', ' ')
            // Canonicalize separators FIRST: every downstream lookup then matches a single dialect.
            .Replace('\\', '/')
            // Cull the path holder so snapshots stay machine-independent.
            .Replace(GetRepoRootPrefixed(), SolutionToken + '/')
            // Our compiler emits its artifacts under a dedicated "myc" tree (myc\Debug\<TFM>) so it
            // never overwrites real SDK output in bin\; map it to the canonical SDK layout. The path
            // may be absolute (".../myc/Debug/...") or relative from the CWD ("myc/Debug/...").
            .Replace("myc/Debug/net11.0/", "bin/Debug/net11.0/")
            // Terminal-logger durations ("succeeded (0.4s)", "in 1.2s") are wall-clock noise that
            // varies per run; collapse any "x.xs" to a token so parity is asserted on structure.
            .ReplaceRegex(@"\d+\.\d+s", "<secs>")
            // The SDK patch version is not ours to pin: CI resolves '11.0.x' to whatever is
            // current, so a snapshot containing one patch fails against a laptop with another. The
            // only three-part version these outputs contain is the SDK's own (no package or TFM
            // versions are printed), so this cannot mask a real behavioural difference.
            .ReplaceRegex(@"\d+\.\d+\.\d+(?=[^\d])", "<sdkver>");

        return string.Join("\n", normalized.Split('\n')
            .Where(line =>
            {
                string trimmed = line.Trim();
                return !trimmed.StartsWith("Total time:")
                       && !trimmed.StartsWith("Solution total time:")
                       && !trimmed.StartsWith("Time Elapsed")
                       && !trimmed.StartsWith("Determining projects to restore")
                       && !trimmed.StartsWith("All projects are up-to-date for restore")
                       && !trimmed.StartsWith("Restored ");
            })
            .Select(line => line.TrimEnd()));
    }

    // Reduces an interactive (pty-captured) build to its stable summary, so ours can be compared
    // with the SDK's terminal logger. The pty captures every spinner frame and per-target task line
    // (names + durations) the logger redraws while compiling; those are transient render noise, so
    // we keep only the terminal-logger grammar that is deterministic: the final status line, error
    // lines, and the "Build succeeded/failed in Ns" summary.
    public static string NormalizeInteractive(string raw)
    {
        var kept = new List<string>();
        foreach (string line in Normalize(raw).Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                // Collapse consecutive blanks so the logger's trailing redraw frames don't matter.
                if (kept.Count > 0 && kept[^1].Length == 0) continue;
                kept.Add(string.Empty);
                continue;
            }

            // The restore banner is a separate lifecycle section of the terminal logger, not part of
            // the compile summary we assert on.
            if (trimmed.StartsWith("Restore ")) continue;

            // Keep the terminal-logger grammar that is deterministic: per-project status lines
            // ("<name> <tfm> succeeded (Xs) -> path" and its "with N warning(s)" variant), the
            // warning/error lines indented under them, and the final status/summary lines.
            bool isSummary = trimmed.Contains("succeeded")
                             || trimmed.Contains("failed with")
                             || trimmed.Contains(": error ")
                             || trimmed.Contains(": warning ");
            if (isSummary) kept.Add(trimmed);
        }

        while (kept.Count > 0 && kept[0].Length == 0) kept.RemoveAt(0);
        while (kept.Count > 0 && kept[^1].Length == 0) kept.RemoveAt(kept.Count - 1);
        return string.Join("\n", kept);
    }

    // Solution builds can't byte-pin against the SDK: both the per-project status lines and the
    // summary's timing clause are state- and timing-dependent (the SDK only leaves per-project
    // lines when a project is visibly compiling, and prints a bare "Build succeeded." - no
    // "in Ns" - when every project is up-to-date). The stable, redirect-safe surface is the
    // verdict word ("Build succeeded."/"Build failed."), any error lines, and the exit code, so
    // solution parity is asserted on that; our full terminal layout is pinned by a snapshot.
    public static string NormalizeSolutionInteractive(string raw)
    {
        var kept = new List<string>();
        foreach (string line in Normalize(raw).Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            if (trimmed.StartsWith("Build succeeded"))
            {
                kept.Add("Build succeeded.");
                continue;
            }
            if (trimmed.StartsWith("Build failed") || trimmed.StartsWith("Build FAILED"))
            {
                kept.Add("Build failed.");
                continue;
            }
            if (trimmed.Contains(": error ")) kept.Add(trimmed);
        }

        return string.Join("\n", kept);
    }

    private static string StripTerminalFrames(string raw)
    {
        string s = raw;
        // OSC sequences (links/progress): ESC ] ... ( BEL | ESC \ ). e.g. "ESC]8;;file://..\a" (hyperlink)
        // and "ESC]9;4;0;ESC\\" (progress bar).
        s = s.ReplaceRegex(@"\x1b\][^\x1b]*(?:\x07|\x1b\\)", string.Empty);
        // CSI sequences (cursor/mode moves): "ESC[?1h" (autowrap) and the logger's cursor frames.
        s = s.ReplaceRegex(@"\x1b\[[0-9;?]*[a-zA-Z]", string.Empty);
        return s.Replace("\x1b", string.Empty);
    }

    private static string ReplaceRegex(this string input, string pattern, string replacement) =>
        Regex.Replace(input, pattern, replacement);

    // Renders two CLI outputs side-by-side for visual behavior-parity snapshots. Column widths and
    // titles are configurable so the same helper is reusable across the whole suite. Columns are
    // auto-sized to the longest line (capped) and lines wider than a column are WRAPPED across extra
    // rows, so no content is ever dropped - a comparison should never hide output.
    private const int AutoWidthCap = 130;

    public static string GenerateSideBySide(
        string leftOutput,
        string rightOutput,
        string leftTitle = "LEGACY CLI",
        string rightTitle = "NEW CLI",
        int columnWidth = 0)
    {
        // Cull the titles too: they carry the fixture path, and the auto-sized column width is
        // derived from the widest rendered line, so an unculled title makes the whole table
        // machine-dependent.
        leftTitle = CullRepoPath(leftTitle);
        rightTitle = CullRepoPath(rightTitle);

        var leftLines = leftOutput.Replace("\r\n", "\n").Split('\n');
        var rightLines = rightOutput.Replace("\r\n", "\n").Split('\n');

        // columnWidth <= 0 means auto-size: fit the widest line, but never balloon beyond the cap
        // (a pathological 1-line output must not stretch the whole table).
        int effectiveWidth = columnWidth > 0
            ? columnWidth
            : Math.Min(Math.Max(ColumnWidth(leftLines, rightLines), 40), AutoWidthCap);

        var sb = new StringBuilder();

        // Dynamically build the format string based on effectiveWidth (replaces hardcoded -%)
        string rowFormat = $"{{0,-{effectiveWidth}}} | {{1,-{effectiveWidth}}}";

        // Header
        sb.AppendLine(string.Format(rowFormat, leftTitle, rightTitle));

        // Separator line (columnWidth * 2 + 3 characters for the " | " divider)
        int separatorLength = (effectiveWidth * 2) + 3;
        sb.AppendLine(new string('-', separatorLength));

        int outerLines = Math.Max(leftLines.Length, rightLines.Length);
        for (int i = 0; i < outerLines; i++)
        {
            string leftLine = i < leftLines.Length ? leftLines[i].TrimEnd('\r') : string.Empty;
            string rightLine = i < rightLines.Length ? rightLines[i].TrimEnd('\r') : string.Empty;

            // Wrap oversized lines instead of truncating so the full text survives in the snapshot.
            string[] leftChunks = Wrap(leftLine, effectiveWidth);
            string[] rightChunks = Wrap(rightLine, effectiveWidth);

            int innerRows = Math.Max(leftChunks.Length, rightChunks.Length);
            for (int j = 0; j < innerRows; j++)
            {
                string l = j < leftChunks.Length ? leftChunks[j] : string.Empty;
                string r = j < rightChunks.Length ? rightChunks[j] : string.Empty;
                sb.AppendLine(string.Format(rowFormat, l, r));
            }
        }

        return sb.ToString();
    }

    private static int ColumnWidth(string[] leftLine, string[] rightLine) =>
        leftLine.Concat(rightLine).Max(line => line.Length);

    private static string[] Wrap(string line, int width)
    {
        if (line.Length <= width) return new[] { line };

        var chunks = new List<string>();
        for (int start = 0; start < line.Length; start += width)
        {
            chunks.Add(line.Substring(start, Math.Min(width, line.Length - start)));
        }

        return chunks.ToArray();
    }
}