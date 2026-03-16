namespace BN.WorkflowDoc.Core.Domain;

public enum ConditionOperator
{
    And,
    Or,
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    Contains,
    BeginsWith,
    EndsWith,
    IsNull,
    IsNotNull,
    Custom
}

public sealed record ConditionNode(
    ConditionOperator Operator,
    string? Left,
    string? Right,
    IReadOnlyList<ConditionNode> Children)
{
    public static ConditionNode Leaf(ConditionOperator op, string? left, string? right) =>
        new(op, left, right, Array.Empty<ConditionNode>());
}

