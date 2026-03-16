using BN.WorkflowDoc.Core.Domain;
using System.Text.RegularExpressions;

namespace BN.WorkflowDoc.Core.Application;

internal static class DeterministicDiagramPaging
{
    private const int MinNodesPerPage = 14;
    private const int MaxNodesPerPage = 24;

    public static IReadOnlyList<DiagramGraph> Split(DiagramGraph diagram)
    {
        if (diagram.Nodes.Count <= MaxNodesPerPage)
        {
            return [diagram];
        }

        var pages = new List<DiagramGraph>();
        var ranges = BuildPageRanges(diagram);
        var totalPages = ranges.Count;

        for (var i = 0; i < totalPages; i++)
        {
            var range = ranges[i];
            var pageNodes = diagram.Nodes
                .Skip(range.StartIndex)
                .Take(range.Length)
                .ToArray();
            var nodeIds = pageNodes
                .Select(x => x.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pageEdges = diagram.Edges
                .Where(x => nodeIds.Contains(x.FromNodeId) && nodeIds.Contains(x.ToNodeId))
                .ToArray();

            pages.Add(new DiagramGraph(
                Type: diagram.Type,
                Nodes: pageNodes,
                Edges: pageEdges,
                Caption: $"{diagram.Caption} ({BuildSegmentTitle(pageNodes)}; View {i + 1} of {totalPages})"));
        }

        return pages;
    }

    private static List<(int StartIndex, int Length)> BuildPageRanges(DiagramGraph diagram)
    {
        var ranges = new List<(int StartIndex, int Length)>();
        var incomingCounts = diagram.Edges
            .GroupBy(x => x.ToNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var outgoingLookup = diagram.Edges
            .GroupBy(x => x.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var sequentialEdges = diagram.Edges
            .Select(x => (x.FromNodeId, x.ToNodeId))
            .ToHashSet();

        var startIndex = 0;
        while (startIndex < diagram.Nodes.Count)
        {
            var remaining = diagram.Nodes.Count - startIndex;
            if (remaining <= MaxNodesPerPage)
            {
                ranges.Add((startIndex, remaining));
                break;
            }

            var maxEndIndex = Math.Min(diagram.Nodes.Count - 1, startIndex + MaxNodesPerPage - 1);
            var minEndIndex = Math.Min(maxEndIndex, startIndex + MinNodesPerPage - 1);
            var cutIndex = ChooseCutIndex(diagram, minEndIndex, maxEndIndex, incomingCounts, outgoingLookup, sequentialEdges);
            ranges.Add((startIndex, cutIndex - startIndex + 1));
            startIndex = cutIndex + 1;
        }

        return ranges;
    }

    private static int ChooseCutIndex(
        DiagramGraph diagram,
        int minEndIndex,
        int maxEndIndex,
        IReadOnlyDictionary<string, int> incomingCounts,
        IReadOnlyDictionary<string, DiagramEdge[]> outgoingLookup,
        HashSet<(string FromNodeId, string ToNodeId)> sequentialEdges)
    {
        var bestIndex = maxEndIndex;
        var bestScore = int.MinValue;

        for (var candidate = minEndIndex; candidate <= maxEndIndex; candidate++)
        {
            var score = ScoreCut(diagram, candidate, incomingCounts, outgoingLookup, sequentialEdges);
            if (score > bestScore || (score == bestScore && candidate > bestIndex))
            {
                bestScore = score;
                bestIndex = candidate;
            }
        }

        return bestIndex;
    }

    private static int ScoreCut(
        DiagramGraph diagram,
        int candidate,
        IReadOnlyDictionary<string, int> incomingCounts,
        IReadOnlyDictionary<string, DiagramEdge[]> outgoingLookup,
        HashSet<(string FromNodeId, string ToNodeId)> sequentialEdges)
    {
        var current = diagram.Nodes[candidate];
        var next = candidate + 1 < diagram.Nodes.Count ? diagram.Nodes[candidate + 1] : null;

        var score = 0;
        if (outgoingLookup.TryGetValue(current.Id, out var outgoingEdges))
        {
            if (outgoingEdges.Length > 1)
            {
                score += 120;
            }

            if (outgoingEdges.Any(x => !string.IsNullOrWhiteSpace(x.Label)))
            {
                score += 90;
            }
        }

        if (incomingCounts.TryGetValue(current.Id, out var currentIncoming) && currentIncoming > 1)
        {
            score += 70;
        }

        if (LooksLikeMerge(current))
        {
            score += 90;
        }

        if (LooksLikeDecision(current))
        {
            score += 60;
        }

        if (next is not null)
        {
            if (incomingCounts.TryGetValue(next.Id, out var nextIncoming) && nextIncoming > 1)
            {
                score += 110;
            }

            if (LooksLikeMerge(next))
            {
                score += 100;
            }

            if (LooksLikeDecision(next))
            {
                score += 50;
            }

            if (!sequentialEdges.Contains((current.Id, next.Id)))
            {
                score += 80;
            }
        }

        score += candidate;
        return score;
    }

    private static bool LooksLikeMerge(DiagramNode node)
    {
        return string.Equals(node.Label, "Merge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.Lane, "Decision Logic", StringComparison.OrdinalIgnoreCase)
                && node.Label.Contains("merge", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDecision(DiagramNode node)
    {
        return string.Equals(node.Lane, "Decision Logic", StringComparison.OrdinalIgnoreCase)
            || node.Label.Contains("condition", StringComparison.OrdinalIgnoreCase)
            || node.Label.Contains("decision", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSegmentTitle(IReadOnlyList<DiagramNode> nodes)
    {
        if (nodes.Count == 0)
        {
            return "Overview";
        }

        var themedTitle = ResolveBusinessSegmentTitle(nodes);
        if (!string.IsNullOrWhiteSpace(themedTitle))
        {
            return themedTitle;
        }

        var decisionNode = nodes.FirstOrDefault(LooksLikeDecision);
        if (decisionNode is not null)
        {
            return $"{NormalizeLabel(decisionNode.Label)} Branch";
        }

        var start = nodes.FirstOrDefault(x => !string.Equals(x.Label, "Trigger", StringComparison.OrdinalIgnoreCase)) ?? nodes[0];
        var end = nodes.LastOrDefault(x => !LooksLikeMerge(x)) ?? nodes[^1];
        if (ReferenceEquals(start, end) || string.Equals(start.Id, end.Id, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeLabel(start.Label);
        }

        return $"{NormalizeLabel(start.Label)} to {NormalizeLabel(end.Label)}";
    }

    private static string NormalizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "Workflow Segment";
        }

        var trimmed = SimplifyLabel(label);
        return trimmed.Length <= 48 ? trimmed : trimmed[..45] + "...";
    }

    private static string? ResolveBusinessSegmentTitle(IReadOnlyList<DiagramNode> nodes)
    {
        var normalizedLabels = nodes
            .Select(x => SimplifyLabel(x.Label))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        var text = string.Join(" ", normalizedLabels);

        foreach (var (keyword, title) in BusinessTitleMap)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }
        }

        if (nodes.Any(LooksLikeDecision))
        {
            return "Decision and Routing";
        }

        return null;
    }

    private static string SimplifyLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var value = label.Trim();
        value = Regex.Replace(value, "^\\[[^\\]]+\\]\\s*", string.Empty);

        var colonIndex = value.IndexOf(':');
        if (colonIndex >= 0 && colonIndex < value.Length - 1)
        {
            var remainder = value[(colonIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(remainder))
            {
                value = remainder;
            }
        }

        value = Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
        value = value.Replace('_', ' ').Replace('-', ' ');
        value = Regex.Replace(value, "\\b(?:Condition|Create|Update|Assign|Delete|Stop|CustomAction|Branch|Convert|SendEmail|SetState|Wait|Merge)Step\\d+\\b", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "\\bStep\\d+\\b", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "\\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(value) ? "Workflow Segment" : value;
    }

    private static readonly (string Keyword, string Title)[] BusinessTitleMap =
    [
        ("duplicate", "Duplicate Check and Routing"),
        ("shadow relationship manager", "Shadow Relationship Manager Follow-up"),
        ("relationship manager assistant", "Relationship Manager Assistant Handling"),
        ("relationship manager", "Relationship Manager Follow-up"),
        ("portfolio manager", "Portfolio Manager Follow-up"),
        ("wealth planner", "Wealth Planner Follow-up"),
        ("planner", "Planner Follow-up"),
        ("approval", "Approval Routing"),
        ("cancel workflow", "Workflow Exit"),
        ("stop workflow", "Workflow Exit"),
        ("external", "Integration Handling"),
        ("integration", "Integration Handling"),
        ("child workflow", "Child Workflow Handoff"),
        ("follow action", "Follow-up Creation"),
        ("create follow", "Follow-up Creation")
    ];
}