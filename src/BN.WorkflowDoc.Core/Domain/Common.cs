namespace BN.WorkflowDoc.Core.Domain;

public enum ExecutionMode
{
    Synchronous,
    Asynchronous
}

public enum ProcessingStatus
{
    Success,
    PartialSuccess,
    Failed
}

public enum WorkflowComponentType
{
    Trigger,
    Condition,
    Action,
    Stop,
    ChildWorkflow,
    ExternalCall
}

