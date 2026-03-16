using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Contracts;

public interface IDiagramGraphMapper
{
    ParseResult<IReadOnlyList<DiagramGraph>> Map(WorkflowDefinition workflow);
}

public interface IDiagramRenderer
{
    Task<ParseResult<IReadOnlyList<RenderedDiagramAsset>>> RenderAsync(
        IReadOnlyList<DiagramGraph> diagrams,
        CancellationToken cancellationToken = default);
}

public enum DiagramDetailLevel
{
    Standard,
    Detailed
}

public sealed record RenderedDiagramAsset(
    DiagramType Type,
    string FileName,
    byte[] Content,
    string ContentType,
    string Caption);

