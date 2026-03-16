using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

internal sealed record DiagramNodeVisualStyle(
    string BadgeLabel,
    string IconSymbol,
    string FillHex,
    string AccentHex,
    string BorderHex,
    string BadgeFillHex,
    string BadgeTextHex,
    string DetailTextHex);

internal static class DiagramVisualStyle
{
    public static DiagramNodeVisualStyle Resolve(WorkflowComponentType componentType)
    {
        return componentType switch
        {
            WorkflowComponentType.Trigger => new DiagramNodeVisualStyle("TRIGGER", "T", "#eef6ff", "#5b8def", "#3f6fca", "#d7e8ff", "#214e9b", "#46648f"),
            WorkflowComponentType.Condition => new DiagramNodeVisualStyle("DECISION", "?", "#fff5dd", "#f0b429", "#d39a15", "#ffe7a8", "#8a5d00", "#7b6642"),
            WorkflowComponentType.Action => new DiagramNodeVisualStyle("ACTION", "A", "#ecfbf4", "#2ea56f", "#268359", "#d2f2e0", "#165a3d", "#43705d"),
            WorkflowComponentType.ChildWorkflow => new DiagramNodeVisualStyle("CHILD", "CW", "#f5efff", "#8a63d2", "#6c46b4", "#e7dcff", "#4c2f86", "#625683"),
            WorkflowComponentType.ExternalCall => new DiagramNodeVisualStyle("EXTERNAL", "EX", "#fff0f0", "#d64545", "#b63636", "#ffd8d8", "#8b1f1f", "#7b5555"),
            WorkflowComponentType.Stop => new DiagramNodeVisualStyle("STOP", "X", "#f3f4f6", "#6b7280", "#4b5563", "#dde1e7", "#374151", "#525a66"),
            _ => new DiagramNodeVisualStyle("STEP", "S", "#f7f8fb", "#7a8798", "#5e6b7b", "#e9edf2", "#3c4653", "#5a6470")
        };
    }

    public static IReadOnlyList<(WorkflowComponentType Type, string Label)> LegendItems { get; } =
    [
        (WorkflowComponentType.Trigger, "Trigger"),
        (WorkflowComponentType.Condition, "Decision"),
        (WorkflowComponentType.Action, "Action"),
        (WorkflowComponentType.ChildWorkflow, "Child Workflow"),
        (WorkflowComponentType.ExternalCall, "External Call"),
        (WorkflowComponentType.Stop, "Stop")
    ];

    public static string ToBadgeLabel(WorkflowComponentType componentType) => Resolve(componentType).BadgeLabel;
}