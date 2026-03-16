using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

internal static class DeterministicDiagramLayout
{
    private const int Margin = 20;
    private const int TitleHeight = 40;
    private const int LaneGap = 30;
    private const int NodeGap = 24;
    private const int LaneHeaderHeight = 30;
    private const int LanePaddingX = 18;
    private const int LaneBottomPadding = 18;
    private const int MinWidth = 420;
    private const int MinHeight = 240;
    private const int CaptionHeight = 28;

    public static DiagramCanvasLayout Build(DiagramGraph diagram)
    {
        var lanes = diagram.Nodes
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Lane) ? "Flow" : x.Lane)
            .ToArray();

        if (lanes.Length == 0)
        {
            return new DiagramCanvasLayout(
                Width: MinWidth,
                Height: MinHeight,
                Margin: Margin,
                Lanes: Array.Empty<DiagramLaneLayout>(),
                NodeLookup: new Dictionary<string, DiagramNodeLayout>(StringComparer.OrdinalIgnoreCase));
        }

        var laneLayouts = new List<DiagramLaneLayout>(lanes.Length);
        var nodeLookup = new Dictionary<string, DiagramNodeLayout>(StringComparer.OrdinalIgnoreCase);
        var laneTop = Margin + TitleHeight;
        var currentLeft = Margin;
        var maxBottom = laneTop;
        var laneIndex = 0;

        foreach (var lane in lanes)
        {
            var nodes = lane
                .Select(node => new
                {
                    Node = node,
                    Width = (int)Math.Round(node.Width <= 0 ? 220 : node.Width),
                    Height = (int)Math.Round(node.Height <= 0 ? 70 : node.Height)
                })
                .ToArray();

            var laneWidth = Math.Max(220, nodes.Length == 0 ? 220 : nodes.Max(x => x.Width) + (LanePaddingX * 2));
            var nodeLayouts = new List<DiagramNodeLayout>(nodes.Length);
            var currentTop = laneTop + LaneHeaderHeight + 10;

            foreach (var node in nodes)
            {
                var x = currentLeft + ((laneWidth - node.Width) / 2);
                var y = currentTop;
                var layout = new DiagramNodeLayout(node.Node, x, y, node.Width, node.Height);
                nodeLayouts.Add(layout);
                nodeLookup[node.Node.Id] = layout;
                currentTop += node.Height + NodeGap;
            }

            var lastBottom = nodeLayouts.Count == 0
                ? laneTop + LaneHeaderHeight + 40
                : nodeLayouts[^1].Y + nodeLayouts[^1].H;
            var laneHeight = Math.Max(150, (lastBottom - laneTop) + LaneBottomPadding);

            laneLayouts.Add(new DiagramLaneLayout(laneIndex, lane.Key, currentLeft, laneTop, laneWidth, laneHeight, nodeLayouts));
            currentLeft += laneWidth + LaneGap;
            maxBottom = Math.Max(maxBottom, laneTop + laneHeight);
            laneIndex++;
        }

        var width = Math.Max(MinWidth, currentLeft - LaneGap + Margin);
        var height = Math.Max(MinHeight, maxBottom + CaptionHeight + Margin);

        return new DiagramCanvasLayout(width, height, Margin, laneLayouts, nodeLookup);
    }
}

internal sealed record DiagramCanvasLayout(
    int Width,
    int Height,
    int Margin,
    IReadOnlyList<DiagramLaneLayout> Lanes,
    IReadOnlyDictionary<string, DiagramNodeLayout> NodeLookup);

internal sealed record DiagramLaneLayout(
    int Index,
    string Name,
    int Left,
    int Top,
    int Width,
    int Height,
    IReadOnlyList<DiagramNodeLayout> Nodes);

internal sealed record DiagramNodeLayout(
    DiagramNode Node,
    int X,
    int Y,
    int W,
    int H);