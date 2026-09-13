using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NativeCompilerTests;

// Renders two CLI outputs side-by-side for visual behavior-parity snapshots. Column widths and
// titles are configurable so the same helper is reusable across the whole suite. Columns are
// auto-sized to the longest line (capped) and lines wider than a column are WRAPPED across extra
// rows, so no content is ever dropped - a comparison should never hide output.
public static class CliComparisonHelper
{
    private const int AutoWidthCap = 130;

    public static string GenerateSideBySide(
        string leftOutput,
        string rightOutput,
        string leftTitle = "LEGACY CLI",
        string rightTitle = "NEW CLI",
        int columnWidth = 0)
    {
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