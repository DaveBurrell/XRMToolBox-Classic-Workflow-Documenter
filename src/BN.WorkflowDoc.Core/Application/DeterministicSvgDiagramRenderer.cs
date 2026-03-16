using System.Text;
using System.Text.Encodings.Web;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

public sealed class DeterministicSvgDiagramRenderer : IDiagramRenderer
{
    private readonly DiagramDetailLevel _detailLevel;

    public DeterministicSvgDiagramRenderer()
        : this(DiagramDetailLevel.Detailed)
    {
    }

    public DeterministicSvgDiagramRenderer(DiagramDetailLevel detailLevel)
    {
        _detailLevel = detailLevel;
    }

    public Task<ParseResult<IReadOnlyList<RenderedDiagramAsset>>> RenderAsync(
        IReadOnlyList<DiagramGraph> diagrams,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<ProcessingWarning>();
        var assets = new List<RenderedDiagramAsset>(diagrams.Count);

        foreach (var diagram in diagrams)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pages = DeterministicDiagramPaging.Split(diagram);

            if (diagram.Nodes.Count == 0)
            {
                warnings.Add(new ProcessingWarning(
                    "DIAGRAM_EMPTY",
                    $"Diagram '{diagram.Type}' has no nodes; rendering minimal placeholder.",
                    diagram.Type.ToString(),
                    false,
                    WarningCategory.Diagram,
                    WarningSeverity.Info));
            }

                    for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
                    {
                    var page = pages[pageIndex];
                    var svg = BuildSvg(page);
                    var bytes = Encoding.UTF8.GetBytes(svg);
                    var fileName = BuildFileName(page, pageIndex, pages.Count);

                    assets.Add(new RenderedDiagramAsset(
                        Type: page.Type,
                        FileName: fileName,
                        Content: bytes,
                        ContentType: "image/svg+xml",
                        Caption: page.Caption));
                    }
        }

        var status = warnings.Count == 0 ? ProcessingStatus.Success : ProcessingStatus.PartialSuccess;
        return Task.FromResult(new ParseResult<IReadOnlyList<RenderedDiagramAsset>>(status, assets, warnings));
    }

    private static string BuildFileName(DiagramGraph diagram, int pageIndex, int pageCount)
    {
        var type = diagram.Type.ToString().ToLowerInvariant();
        return pageCount <= 1
            ? $"{type}.svg"
            : $"{type}-part-{pageIndex + 1:D2}.svg";
    }

    private string BuildSvg(DiagramGraph diagram)
    {
        var layout = DeterministicDiagramLayout.Build(diagram);

        var encoder = JavaScriptEncoder.Default;
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns='http://www.w3.org/2000/svg' width='{layout.Width}' height='{layout.Height}' viewBox='0 0 {layout.Width} {layout.Height}'>");
        sb.AppendLine("  <defs>");
        sb.AppendLine("    <marker id='arrow' viewBox='0 0 10 10' refX='8' refY='5' markerWidth='8' markerHeight='8' orient='auto-start-reverse'>");
        sb.AppendLine("      <path d='M 0 0 L 10 5 L 0 10 z' fill='#3a485e' />");
        sb.AppendLine("    </marker>");
        sb.AppendLine("    <linearGradient id='bg' x1='0%' y1='0%' x2='0%' y2='100%'>");
        sb.AppendLine("      <stop offset='0%' stop-color='#f9fbff' />");
        sb.AppendLine("      <stop offset='100%' stop-color='#f1f5fb' />");
        sb.AppendLine("    </linearGradient>");
        sb.AppendLine("    <filter id='softShadow' x='-20%' y='-20%' width='140%' height='140%'>");
        sb.AppendLine("      <feDropShadow dx='0' dy='3' stdDeviation='3' flood-color='#9aa8bc' flood-opacity='0.35' />");
        sb.AppendLine("    </filter>");
        sb.AppendLine("  </defs>");
        sb.AppendLine($"  <rect x='1' y='1' width='{layout.Width - 2}' height='{layout.Height - 2}' fill='url(#bg)' stroke='#d6e0ef' />");
        sb.AppendLine($"  <text x='{layout.Margin}' y='{layout.Margin}' font-size='16' font-family='Segoe UI, Arial' font-weight='600'>{encoder.Encode(diagram.Type.ToString())}</text>");
        sb.AppendLine($"  <text x='{layout.Margin}' y='{layout.Margin + 18}' font-size='10' font-family='Segoe UI, Arial' fill='#5a6778'>Colour-coded by workflow action type with embedded node details</text>");
        AppendLegend(sb, encoder, layout);

        foreach (var edge in diagram.Edges)
        {
            if (!layout.NodeLookup.TryGetValue(edge.FromNodeId, out var from) || !layout.NodeLookup.TryGetValue(edge.ToNodeId, out var to))
            {
                continue;
            }

            var x1 = from.X + (from.W / 2);
            var y1 = from.Y + from.H;
            var x2 = to.X + (to.W / 2);
            var y2 = to.Y;
            sb.AppendLine($"  <line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='#4a5668' stroke-width='2' marker-end='url(#arrow)' />");

            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                var midX = (x1 + x2) / 2;
                var midY = (y1 + y2) / 2 - 6;
                sb.AppendLine($"  <rect x='{midX - 34}' y='{midY - 12}' rx='4' ry='4' width='68' height='18' fill='rgba(255,255,255,0.92)' stroke='#d1d9e4' />");
                sb.AppendLine($"  <text x='{midX}' y='{midY + 1}' text-anchor='middle' font-size='11' font-family='Segoe UI, Arial' fill='#3a485e'>{encoder.Encode(edge.Label)}</text>");
            }
        }

        foreach (var lane in layout.Lanes)
        {
            var laneFill = ResolveLaneFill(lane.Index);
            var laneStroke = ResolveLaneStroke(lane.Index);
            sb.AppendLine($"  <rect x='{lane.Left}' y='{lane.Top}' width='{lane.Width}' height='{lane.Height}' fill='{laneFill}' stroke='{laneStroke}' />");
            sb.AppendLine($"  <rect x='{lane.Left}' y='{lane.Top}' width='{lane.Width}' height='28' fill='#e8eef8' stroke='none' />");
            sb.AppendLine($"  <text x='{lane.Left + 10}' y='{lane.Top + 22}' font-size='12' font-family='Segoe UI, Arial' fill='#2a4368'>{encoder.Encode(lane.Name)}</text>");

            foreach (var node in lane.Nodes)
            {
                var nodeStyle = DiagramVisualStyle.Resolve(node.Node.ComponentType);
                sb.AppendLine($"  <rect x='{node.X + 3}' y='{node.Y + 4}' rx='10' ry='10' width='{node.W}' height='{node.H}' fill='rgba(154,168,188,0.35)' stroke='none' />");
                AppendNodeShape(sb, node, nodeStyle, shadowed: true);
                if (node.Node.ComponentType != WorkflowComponentType.Condition)
                {
                    sb.AppendLine($"  <rect x='{node.X}' y='{node.Y}' rx='10' ry='10' width='6' height='{node.H}' fill='{nodeStyle.AccentHex}' stroke='none' />");
                }

                sb.AppendLine($"  <rect x='{node.X + 10}' y='{node.Y + 8}' rx='5' ry='5' width='{Math.Min(node.W - 20, 84)}' height='18' fill='{nodeStyle.BadgeFillHex}' stroke='none' />");
                sb.AppendLine($"  <text x='{node.X + 16}' y='{node.Y + 21}' font-size='8.5' font-family='Segoe UI, Arial' font-weight='700' fill='{nodeStyle.BadgeTextHex}'>{encoder.Encode(nodeStyle.BadgeLabel)}</text>");

                var iconCx = node.X + node.W - 18;
                var iconCy = node.Y + 17;
                sb.AppendLine($"  <circle cx='{iconCx}' cy='{iconCy}' r='8.5' fill='{nodeStyle.AccentHex}' stroke='{nodeStyle.BorderHex}' />");
                sb.AppendLine($"  <text x='{iconCx}' y='{iconCy + 3}' text-anchor='middle' font-size='8' font-family='Segoe UI, Arial' font-weight='700' fill='#ffffff'>{encoder.Encode(nodeStyle.IconSymbol)}</text>");

                var textLeft = node.Node.ComponentType == WorkflowComponentType.Condition ? node.X + 34 : node.X + 10;
                var textWidth = node.Node.ComponentType == WorkflowComponentType.Condition ? node.W - 68 : node.W - 20;
                AppendWrappedText(sb, encoder, node.Node.Label, textLeft, node.Y + 40, textWidth, 12, "#1f2d3d", 1, true);

                var detailLines = ResolveDetailLines(node.Node);
                var detailY = node.Y + 60;
                foreach (var detail in detailLines)
                {
                    AppendWrappedText(sb, encoder, detail, textLeft, detailY, textWidth, 10, nodeStyle.DetailTextHex, 2, false);
                    detailY += 18;
                }
            }
        }

        sb.AppendLine($"  <text x='{layout.Margin}' y='{layout.Height - 12}' font-size='11' font-family='Segoe UI, Arial' fill='#6a6a6a'>{encoder.Encode(diagram.Caption)}</text>");
        sb.AppendLine("</svg>");

        return sb.ToString();
    }

    private static string ResolveLaneFill(int laneIndex)
    {
        return (laneIndex % 3) switch
        {
            0 => "#f4f8ff",
            1 => "#f5fcf9",
            _ => "#fcf8f4"
        };
    }

    private static string ResolveLaneStroke(int laneIndex)
    {
        return (laneIndex % 3) switch
        {
            0 => "#c0d2ec",
            1 => "#bee0ce",
            _ => "#ebd4be"
        };
    }

    private static void AppendWrappedText(StringBuilder sb, JavaScriptEncoder encoder, string text, int x, int y, int width, int fontSize = 12, string fill = "#1f2d3d", int maxLines = 4, bool bold = false)
    {
        var lines = WrapText(text, width, maxLines);
        if (lines.Count == 0)
        {
            return;
        }

        var fontWeight = bold ? " font-weight='600'" : string.Empty;
        sb.AppendLine($"  <text x='{x}' y='{y}' font-size='{fontSize}' font-family='Segoe UI, Arial' fill='{fill}'{fontWeight}>");
        for (var i = 0; i < lines.Count; i++)
        {
            var dy = i == 0 ? 0 : Math.Max(12, fontSize + 3);
            sb.AppendLine($"    <tspan x='{x}' dy='{dy}'>{encoder.Encode(lines[i])}</tspan>");
        }

        sb.AppendLine("  </text>");
    }

    private static IReadOnlyList<string> WrapText(string text, int width, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var maxCharsPerLine = Math.Max(12, width / 7);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        var current = words[0];

        for (var i = 1; i < words.Length; i++)
        {
            var candidate = $"{current} {words[i]}";
            if (candidate.Length <= maxCharsPerLine)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            if (lines.Count == maxLines - 1)
            {
                current = string.Join(' ', words[i..]);
                if (current.Length > maxCharsPerLine)
                {
                    current = current[..Math.Max(0, maxCharsPerLine - 3)] + "...";
                }

                lines.Add(current);
                return lines;
            }

            current = words[i];
        }

        lines.Add(current);
        return lines;
    }

    private static void AppendLegend(StringBuilder sb, JavaScriptEncoder encoder, DiagramCanvasLayout layout)
    {
        var legendX = layout.Width - 360;
        var legendY = layout.Margin + 6;
        sb.AppendLine($"  <rect x='{legendX}' y='{legendY}' rx='8' ry='8' width='330' height='34' fill='rgba(255,255,255,0.88)' stroke='#d1d9e4' />");

        var cursorX = legendX + 10;
        foreach (var legendItem in DiagramVisualStyle.LegendItems)
        {
            var style = DiagramVisualStyle.Resolve(legendItem.Type);
            sb.AppendLine($"  <rect x='{cursorX}' y='{legendY + 9}' rx='3' ry='3' width='14' height='14' fill='{style.AccentHex}' stroke='{style.BorderHex}' />");
            sb.AppendLine($"  <text x='{cursorX + 7}' y='{legendY + 20}' text-anchor='middle' font-size='8' font-family='Segoe UI, Arial' font-weight='700' fill='#ffffff'>{encoder.Encode(style.IconSymbol)}</text>");
            sb.AppendLine($"  <text x='{cursorX + 20}' y='{legendY + 20}' font-size='10' font-family='Segoe UI, Arial' fill='#4a5668'>{encoder.Encode(legendItem.Label)}</text>");
            cursorX += legendItem.Label.Length * 6 + 40;
            if (cursorX > legendX + 280)
            {
                break;
            }
        }
    }

    private static void AppendNodeShape(StringBuilder sb, DiagramNodeLayout node, DiagramNodeVisualStyle style, bool shadowed)
    {
        var shadowFilter = shadowed ? " filter='url(#softShadow)'" : string.Empty;
        if (node.Node.ComponentType == WorkflowComponentType.Condition)
        {
            var cx = node.X + (node.W / 2);
            var cy = node.Y + (node.H / 2);
            var points = $"{cx},{node.Y} {node.X + node.W},{cy} {cx},{node.Y + node.H} {node.X},{cy}";
            sb.AppendLine($"  <polygon points='{points}' fill='{style.FillHex}' stroke='{style.BorderHex}' stroke-width='2'{shadowFilter} />");
            return;
        }

        if (node.Node.ComponentType == WorkflowComponentType.Stop)
        {
            var radius = Math.Max(16, node.H / 2);
            sb.AppendLine($"  <rect x='{node.X}' y='{node.Y}' rx='{radius}' ry='{radius}' width='{node.W}' height='{node.H}' fill='{style.FillHex}' stroke='{style.BorderHex}' stroke-width='2'{shadowFilter} />");
            return;
        }

        sb.AppendLine($"  <rect x='{node.X}' y='{node.Y}' rx='10' ry='10' width='{node.W}' height='{node.H}' fill='{style.FillHex}' stroke='{style.BorderHex}' stroke-width='2'{shadowFilter} />");
    }

    private IReadOnlyList<string> ResolveDetailLines(DiagramNode node)
    {
        var lines = node.DetailLines?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray() ?? Array.Empty<string>();

        if (_detailLevel == DiagramDetailLevel.Standard)
        {
            return lines.Take(1).ToArray();
        }

        return lines.Take(3).ToArray();
    }
}

