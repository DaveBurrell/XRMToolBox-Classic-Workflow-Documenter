using System.Text.RegularExpressions;

namespace BN.WorkflowDoc.Wpf;

internal static class MainWindowFormatting
{
    private static readonly Regex WarningCodeRegex = new(@"^\[(?<code>[^\]]+)\]", RegexOptions.Compiled);

    internal static List<string> BuildWarningSummary(IReadOnlyList<string> warnings)
    {
        var grouped = warnings
            .GroupBy(ExtractWarningCode, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"[{g.Key}] x{g.Count()}")
            .ToList();

        return grouped.Count == 0 ? ["No warnings"] : grouped;
    }

    internal static string BuildBatchSummaryLine(string status, int workflowCount, int warningCount, TimeSpan duration)
    {
        var durationText = duration.TotalSeconds < 1
            ? "<1s"
            : $"{duration.TotalSeconds:F1}s";

        return $"Status: {status} | Workflows: {workflowCount} | Warnings: {warningCount} | Duration: {durationText}";
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string ExtractWarningCode(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return "UNKNOWN";
        }

        var match = WarningCodeRegex.Match(warning);
        return match.Success ? match.Groups["code"].Value : "UNKNOWN";
    }
}
