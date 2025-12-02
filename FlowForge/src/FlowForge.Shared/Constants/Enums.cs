namespace FlowForge.Shared.Constants;

/// <summary>
/// Represents the execution status of a workflow instance.
/// </summary>
public enum WorkflowStatus
{
    /// <summary>Workflow is created but not yet started.</summary>
    Pending = 0,
    
    /// <summary>Workflow is scheduled and waiting in queue.</summary>
    Scheduled = 1,
    
    /// <summary>Workflow is currently being executed.</summary>
    Running = 2,
    
    /// <summary>Workflow is paused and waiting for external input or timer.</summary>
    Suspended = 3,
    
    /// <summary>Workflow completed successfully.</summary>
    Completed = 4,
    
    /// <summary>Workflow failed due to an error.</summary>
    Failed = 5,
    
    /// <summary>Workflow was cancelled by user or system.</summary>
    Cancelled = 6,
    
    /// <summary>Workflow timed out.</summary>
    TimedOut = 7
}

/// <summary>
/// Represents the execution status of an activity within a workflow.
/// </summary>
public enum ActivityStatus
{
    /// <summary>Activity is waiting to be executed.</summary>
    Pending = 0,
    
    /// <summary>Activity is currently executing.</summary>
    Running = 1,
    
    /// <summary>Activity completed successfully.</summary>
    Completed = 2,
    
    /// <summary>Activity failed and may be retried.</summary>
    Failed = 3,
    
    /// <summary>Activity was skipped due to condition.</summary>
    Skipped = 4,
    
    /// <summary>Activity is waiting for retry after failure.</summary>
    Retrying = 5,
    
    /// <summary>Activity was cancelled.</summary>
    Cancelled = 6
}

/// <summary>
/// Workflow trigger types.
/// </summary>
public enum TriggerType
{
    /// <summary>Triggered manually via API.</summary>
    Manual = 0,
    
    /// <summary>Triggered on a schedule (cron).</summary>
    Scheduled = 1,
    
    /// <summary>Triggered by webhook.</summary>
    Webhook = 2,
    
    /// <summary>Triggered by an event from message queue.</summary>
    Event = 3,
    
    /// <summary>Triggered by another workflow.</summary>
    Workflow = 4
}

/// <summary>
/// Worker node status.
/// </summary>
public enum WorkerStatus
{
    /// <summary>Worker is online and accepting work.</summary>
    Online = 0,
    
    /// <summary>Worker is online but not accepting new work.</summary>
    Draining = 1,
    
    /// <summary>Worker is offline.</summary>
    Offline = 2,
    
    /// <summary>Worker health check failed.</summary>
    Unhealthy = 3
}
