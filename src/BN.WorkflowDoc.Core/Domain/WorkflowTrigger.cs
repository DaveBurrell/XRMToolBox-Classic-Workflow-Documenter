namespace BN.WorkflowDoc.Core.Domain;

public sealed record WorkflowTrigger(
    string PrimaryEntity,
    bool OnCreate,
    bool OnUpdate,
    bool OnDelete,
    IReadOnlyList<string> AttributeFilters,
    string? TriggerDescription);

