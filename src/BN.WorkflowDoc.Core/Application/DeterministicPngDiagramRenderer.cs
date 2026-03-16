using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;

namespace BN.WorkflowDoc.Core.Application;

public sealed class DeterministicPngDiagramRenderer : IDiagramRenderer
{
    private static readonly ConcurrentDictionary<string, Font?> FontCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DiagramDetailLevel _detailLevel;

    public DeterministicPngDiagramRenderer()
        : this(DiagramDetailLevel.Detailed)
    {
    }

    public DeterministicPngDiagramRenderer(DiagramDetailLevel detailLevel)
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
                var bytes = BuildPng(page);
                var fileName = pages.Count == 1
                    ? $"{diagram.Type.ToString().ToLowerInvariant()}.png"
                    : $"{diagram.Type.ToString().ToLowerInvariant()}-part-{pageIndex + 1:D2}.png";

                assets.Add(new RenderedDiagramAsset(
                    Type: diagram.Type,
                    FileName: fileName,
                    Content: bytes,
                    ContentType: "image/png",
                    Caption: page.Caption));
            }
        }

        var status = warnings.Count == 0 ? ProcessingStatus.Success : ProcessingStatus.PartialSuccess;
        return Task.FromResult(new ParseResult<IReadOnlyList<RenderedDiagramAsset>>(status, assets, warnings));
    }

    private byte[] BuildPng(DiagramGraph diagram)
    {
        var layout = DeterministicDiagramLayout.Build(diagram);

        var titleFont = ResolveFont(16F, FontStyle.Bold);
        var subtitleFont = ResolveFont(10F);
        var laneFont = ResolveFont(11F, FontStyle.Italic);
        var nodeFont = ResolveFont(11F, FontStyle.Bold);
        var nodeMetaFont = ResolveFont(9.5F);
        var badgeFont = ResolveFont(8.5F, FontStyle.Bold);
        var iconFont = ResolveFont(8F, FontStyle.Bold);
        var captionFont = ResolveFont(10F);

        using var image = new Image<Rgba32>(layout.Width, layout.Height, new Rgba32(255, 255, 255));
        image.Mutate(ctx =>
        {
            ctx.Fill(new LinearGradientBrush(
                    new PointF(0, 0),
                    new PointF(0, layout.Height),
                    GradientRepetitionMode.None,
                    new ColorStop(0f, new Rgba32(250, 252, 255)),
                    new ColorStop(1f, new Rgba32(242, 246, 251))),
                new RectangleF(0, 0, layout.Width, layout.Height));
            ctx.Draw(new Rgba32(210, 220, 234), 1.2f, new RectangleF(1, 1, layout.Width - 2, layout.Height - 2));

            if (titleFont is not null)
            {
                ctx.DrawText(diagram.Type.ToString(), titleFont, new Rgba32(31, 45, 61), new PointF(layout.Margin, layout.Margin + 2));
            }

            if (subtitleFont is not null)
            {
                ctx.DrawText("Colour-coded by workflow action type with embedded node details", subtitleFont, new Rgba32(90, 103, 120), new PointF(layout.Margin, layout.Margin + 22));
            }

            DrawLegend(ctx, layout, badgeFont, subtitleFont);
            var edgeColor = new Rgba32(74, 86, 104);

            foreach (var edge in diagram.Edges)
            {
                if (!layout.NodeLookup.TryGetValue(edge.FromNodeId, out var from) || !layout.NodeLookup.TryGetValue(edge.ToNodeId, out var to))
                {
                    continue;
                }

                var x1 = from.X + (from.W / 2f);
                var y1 = from.Y + from.H;
                var x2 = to.X + (to.W / 2f);
                var y2 = to.Y;
                ctx.DrawLine(edgeColor, 2.1f, new PointF(x1, y1), new PointF(x2, y2));
                DrawArrowHead(ctx, x1, y1, x2, y2, edgeColor);

                if (!string.IsNullOrWhiteSpace(edge.Label) && captionFont is not null)
                {
                    var labelRect = new RectangleF(((x1 + x2) / 2f) - 34, ((y1 + y2) / 2f) - 14, 68, 18);
                    ctx.Fill(new Rgba32(255, 255, 255, 235), labelRect);
                    ctx.Draw(new Rgba32(205, 213, 224), 1f, labelRect);
                    ctx.DrawText(edge.Label, captionFont, new Rgba32(58, 72, 94), new PointF(labelRect.Left + 6, labelRect.Top + 2));
                }
            }

            foreach (var lane in layout.Lanes)
            {
                var laneFill = ResolveLaneFill(lane.Index);
                var laneStroke = ResolveLaneStroke(lane.Index);
                ctx.Fill(laneFill, new RectangleF(lane.Left, lane.Top, lane.Width, lane.Height));
                ctx.Draw(laneStroke, 1.4f, new RectangleF(lane.Left, lane.Top, lane.Width, lane.Height));
                ctx.Fill(new Rgba32(231, 237, 247), new RectangleF(lane.Left, lane.Top, lane.Width, 30));

                if (laneFont is not null)
                {
                    ctx.DrawText(lane.Name, laneFont, new Rgba32(42, 67, 104), new PointF(lane.Left + 8, lane.Top + 7));
                }

                foreach (var node in lane.Nodes)
                {
                    var nodeStyle = DiagramVisualStyle.Resolve(node.Node.ComponentType);
                    var nodeFill = ParseHex(nodeStyle.FillHex);
                    var accent = ParseHex(nodeStyle.AccentHex);
                    var border = ParseHex(nodeStyle.BorderHex);
                    var badgeFill = ParseHex(nodeStyle.BadgeFillHex);
                    var badgeText = ParseHex(nodeStyle.BadgeTextHex);
                    var detailText = ParseHex(nodeStyle.DetailTextHex);
                    DrawNodeBackground(ctx, node, nodeFill, border, accent);

                    var badgeRect = new RectangleF(node.X + 10, node.Y + 8, Math.Min(node.W - 20, 84), 18);
                    ctx.Fill(badgeFill, badgeRect);
                    if (badgeFont is not null)
                    {
                        ctx.DrawText(DiagramVisualStyle.ToBadgeLabel(node.Node.ComponentType), badgeFont, badgeText, new PointF(badgeRect.Left + 6, badgeRect.Top + 3));
                    }

                    var iconCenter = new PointF(node.X + node.W - 18, node.Y + 17);
                    var iconEllipse = new EllipsePolygon(iconCenter, 8.5f);
                    ctx.Fill(accent, iconEllipse);
                    ctx.Draw(border, 1f, iconEllipse);
                    if (iconFont is not null)
                    {
                        var iconText = nodeStyle.IconSymbol;
                        var iconX = iconCenter.X - ((iconText.Length == 1 ? 2.8f : 5.6f) * iconText.Length / Math.Max(1, iconText.Length));
                        ctx.DrawText(iconText, iconFont, new Rgba32(255, 255, 255), new PointF(iconX, iconCenter.Y - 4));
                    }

                    if (nodeFont is not null)
                    {
                        var textLeft = node.Node.ComponentType == WorkflowComponentType.Condition ? node.X + 34 : node.X + 10;
                        var textWidth = node.Node.ComponentType == WorkflowComponentType.Condition ? node.W - 68 : node.W - 20;
                        var textOpts = new RichTextOptions(nodeFont)
                        {
                            Origin = new PointF(textLeft, node.Y + 32),
                            WrappingLength = textWidth
                        };
                        ctx.DrawText(textOpts, node.Node.Label, new SolidBrush(new Rgba32(31, 45, 61)));
                    }

                    if (nodeMetaFont is not null)
                    {
                        var detailLines = ResolveDetailLines(node.Node);
                        var detailTop = node.Y + 58;
                        var detailLeft = node.Node.ComponentType == WorkflowComponentType.Condition ? node.X + 34 : node.X + 10;
                        var detailWidth = node.Node.ComponentType == WorkflowComponentType.Condition ? node.W - 68 : node.W - 20;
                        foreach (var detail in detailLines)
                        {
                            var detailOpts = new RichTextOptions(nodeMetaFont)
                            {
                                Origin = new PointF(detailLeft, detailTop),
                                WrappingLength = detailWidth
                            };
                            ctx.DrawText(detailOpts, detail, new SolidBrush(detailText));
                            detailTop += 15;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(diagram.Caption) && captionFont is not null)
            {
                ctx.DrawText(diagram.Caption, captionFont, new Rgba32(106, 106, 106), new PointF(layout.Margin, layout.Height - 18));
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static Font? ResolveFont(float size, FontStyle style = FontStyle.Regular)
    {
        var cacheKey = $"{size:0.##}:{style}";
        return FontCache.GetOrAdd(cacheKey, _ => ResolveFontUncached(size, style));
    }

    private static Font? ResolveFontUncached(float size, FontStyle style)
    {
        ReadOnlySpan<string> candidates = ["Segoe UI", "Arial", "Helvetica Neue", "Helvetica", "DejaVu Sans", "Liberation Sans", "Verdana", "Tahoma"];
        foreach (var name in candidates)
        {
            if (SystemFonts.TryGet(name, out var family))
            {
                return family.CreateFont(size, style);
            }
        }

        var fallback = SystemFonts.Families.FirstOrDefault();
        return fallback == default ? null : fallback.CreateFont(size, style);
    }

    private static Rgba32 ResolveLaneFill(int laneIndex)
    {
        return (laneIndex % 3) switch
        {
            0 => new Rgba32(244, 248, 255),
            1 => new Rgba32(245, 252, 249),
            _ => new Rgba32(252, 248, 244)
        };
    }

    private static Rgba32 ResolveLaneStroke(int laneIndex)
    {
        return (laneIndex % 3) switch
        {
            0 => new Rgba32(192, 210, 236),
            1 => new Rgba32(190, 224, 206),
            _ => new Rgba32(235, 212, 190)
        };
    }

    private static void DrawArrowHead(IImageProcessingContext ctx, float x1, float y1, float x2, float y2, Rgba32 color)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001f)
        {
            return;
        }

        var ux = dx / length;
        var uy = dy / length;
        var size = 8f;
        var leftX = x2 - (ux * size) + (uy * (size * 0.55f));
        var leftY = y2 - (uy * size) - (ux * (size * 0.55f));
        var rightX = x2 - (ux * size) - (uy * (size * 0.55f));
        var rightY = y2 - (uy * size) + (ux * (size * 0.55f));

        ctx.DrawLine(color, 2.0f, new PointF(x2, y2), new PointF(leftX, leftY));
        ctx.DrawLine(color, 2.0f, new PointF(x2, y2), new PointF(rightX, rightY));
    }

    private static void DrawLegend(IImageProcessingContext ctx, DiagramCanvasLayout layout, Font? badgeFont, Font? bodyFont)
    {
        var legendX = layout.Width - 360;
        var legendY = layout.Margin + 6;
        ctx.Fill(new Rgba32(255, 255, 255, 220), new RectangleF(legendX, legendY, 330, 34));
        ctx.Draw(new Rgba32(209, 217, 228), 1f, new RectangleF(legendX, legendY, 330, 34));

        var cursorX = legendX + 10;
        foreach (var legendItem in DiagramVisualStyle.LegendItems)
        {
            var style = DiagramVisualStyle.Resolve(legendItem.Type);
            var chipRect = new RectangleF(cursorX, legendY + 8, 14, 14);
            ctx.Fill(ParseHex(style.AccentHex), chipRect);
            ctx.Draw(ParseHex(style.BorderHex), 1f, chipRect);

            if (badgeFont is not null)
            {
                var iconX = cursorX + (style.IconSymbol.Length == 1 ? 4 : 1);
                ctx.DrawText(style.IconSymbol, badgeFont, new Rgba32(255, 255, 255), new PointF(iconX, legendY + 10));
            }

            if (bodyFont is not null)
            {
                ctx.DrawText(legendItem.Label, bodyFont, new Rgba32(74, 86, 104), new PointF(cursorX + 20, legendY + 6));
            }

            cursorX += legendItem.Label.Length * 7 + 42;
            if (cursorX > legendX + 280)
            {
                break;
            }
        }
    }

    private static Rgba32 ParseHex(string hex)
    {
        if (Color.TryParseHex(hex, out var color))
        {
            return color.ToPixel<Rgba32>();
        }

        return new Rgba32(255, 255, 255);
    }

    private static Rgba32 WithAlpha(Rgba32 color, byte alpha)
    {
        return new Rgba32(color.R, color.G, color.B, alpha);
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

    private static void DrawNodeBackground(IImageProcessingContext ctx, DiagramNodeLayout node, Rgba32 nodeFill, Rgba32 border, Rgba32 accent)
    {
        var shadow = new Rgba32(170, 180, 194, 90);
        var rect = new RectangleF(node.X, node.Y, node.W, node.H);
        if (node.Node.ComponentType == WorkflowComponentType.Condition)
        {
            var cx = node.X + (node.W / 2f);
            var cy = node.Y + (node.H / 2f);
            var shadowPolygon = new PointF[]
            {
                new(cx + 3, node.Y + 4),
                new(node.X + node.W + 3, cy + 4),
                new(cx + 3, node.Y + node.H + 4),
                new(node.X + 3, cy + 4)
            };
            var polygon = new PointF[]
            {
                new(cx, node.Y),
                new(node.X + node.W, cy),
                new(cx, node.Y + node.H),
                new(node.X, cy)
            };
            ctx.Fill(shadow, new Polygon(new LinearLineSegment(shadowPolygon)));
            ctx.Fill(nodeFill, new Polygon(new LinearLineSegment(polygon)));
            ctx.Draw(border, 2.0f, new Polygon(new LinearLineSegment(polygon)));
            return;
        }

        if (node.Node.ComponentType == WorkflowComponentType.Stop)
        {
            var radius = MathF.Max(14f, node.H / 2f);
            var centerWidth = Math.Max(1, node.W - (int)(2 * radius));

            var shadowCenterRect = new RectangleF(node.X + 3 + radius, node.Y + 4, centerWidth, node.H);
            var shadowLeftEllipse = new EllipsePolygon(new PointF(node.X + 3 + radius, node.Y + 4 + (node.H / 2f)), radius);
            var shadowRightEllipse = new EllipsePolygon(new PointF(node.X + 3 + node.W - radius, node.Y + 4 + (node.H / 2f)), radius);
            ctx.Fill(shadow, shadowCenterRect);
            ctx.Fill(shadow, shadowLeftEllipse);
            ctx.Fill(shadow, shadowRightEllipse);

            var centerRect = new RectangleF(node.X + radius, node.Y, centerWidth, node.H);
            var leftEllipse = new EllipsePolygon(new PointF(node.X + radius, node.Y + (node.H / 2f)), radius);
            var rightEllipse = new EllipsePolygon(new PointF(node.X + node.W - radius, node.Y + (node.H / 2f)), radius);

            ctx.Fill(nodeFill, centerRect);
            ctx.Fill(nodeFill, leftEllipse);
            ctx.Fill(nodeFill, rightEllipse);

            ctx.Draw(border, 2.0f, leftEllipse);
            ctx.Draw(border, 2.0f, rightEllipse);
            ctx.DrawLine(border, 2.0f, new PointF(node.X + radius, node.Y), new PointF(node.X + node.W - radius, node.Y));
            ctx.DrawLine(border, 2.0f, new PointF(node.X + radius, node.Y + node.H), new PointF(node.X + node.W - radius, node.Y + node.H));

            var accentWidth = 8f;
            ctx.Fill(WithAlpha(accent, 40), new RectangleF(node.X + radius - (accentWidth / 2f), node.Y, accentWidth, node.H));
            return;
        }

        ctx.Fill(shadow, new RectangleF(node.X + 3, node.Y + 4, node.W, node.H));
        ctx.Fill(nodeFill, rect);
        ctx.Fill(WithAlpha(accent, 40), new RectangleF(node.X, node.Y, 6, node.H));
        ctx.Draw(border, 2.0f, rect);
    }
}

