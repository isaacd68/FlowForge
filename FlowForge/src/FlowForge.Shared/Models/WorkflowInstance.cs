using FlowForge.Shared.Constants;

namespace FlowForge.Shared.Models;

/// <summary>
/// Represents an instance of a workflow execution.
/// </summary>
public class WorkflowInstance
{
    /// <summary>Unique identifier for this workflow instance.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>Reference to the workflow definition.</summary>
    public required string WorkflowName { get; set; }
    
    /// <summary>Version of the workflow definition.</summary>
    public int WorkflowVersion { get; set; } = 1;
    
    /// <summary>Current execution status.</summary>
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Pending;
    
    /// <summary>Input data provided when workflow was started.</summary>
    public Dictionary<string, object?> Input { get; set; } = new();
    
    /// <summary>Output data produced by the workflow.</summary>
    public Dictionary<string, object?> Output { get; set; } = new();
    
    /// <summary>Current workflow state/context data.</summary>
    public Dictionary<string, object?> State { get; set; } = new();
    
    /// <summary>Error information if workflow failed.</summary>
    public WorkflowError? Error { get; set; }
    
    /// <summary>Parent workflow instance ID if this is a child workflow.</summary>
    public Guid? ParentInstanceId { get; set; }
    
    /// <summary>Correlation ID for tracing related workflows.</summary>
    public string? CorrelationId { get; set; }
    
    /// <summary>ID of the currently executing activity.</summary>
    public string? CurrentActivityId { get; set; }
    
    /// <summary>When the workflow was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>When the workflow started executing.</summary>
    public DateTimeOffset? StartedAt { get; set; }
    
    /// <summary>When the workflow completed (success or failure).</summary>
    public DateTimeOffset? CompletedAt { get; set; }
    
    /// <summary>Last time the workflow was updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>Number of retry attempts for the current activity.</summary>
    public int RetryCount { get; set; }
    
    /// <summary>Worker ID that is processing this workflow.</summary>
    public string? WorkerId { get; set; }
    
    /// <summary>Tags for categorization and filtering.</summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>Custom metadata.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Represents an error that occurred during workflow execution.
/// </summary>
public class WorkflowError
{
    /// <summary>Error code for categorization.</summary>
    public required string Code { get; set; }
    
    /// <summary>Human-readable error message.</summary>
    public required string Message { get; set; }
    
    /// <summary>Activity where the error occurred.</summary>
    public string? ActivityId { get; set; }
    
    /// <summary>Stack trace if available.</summary>
    public string? StackTrace { get; set; }
    
    /// <summary>When the error occurred.</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>Inner error details.</summary>
    public WorkflowError? InnerError { get; set; }
}

/// <summary>
/// Represents execution history of an activity.
/// </summary>
public class ActivityExecution
{
    /// <summary>Unique identifier for this execution record.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>Reference to the workflow instance.</summary>
    public Guid WorkflowInstanceId { get; set; }
    
    /// <summary>Activity identifier within the workflow.</summary>
    public required string ActivityId { get; set; }
    
    /// <summary>Type of activity that was executed.</summary>
    public required string ActivityType { get; set; }
    
    /// <summary>Execution status.</summary>
    public ActivityStatus Status { get; set; }
    
    /// <summary>Input provided to the activity.</summary>
    public Dictionary<string, object?> Input { get; set; } = new();
    
    /// <summary>Output produced by the activity.</summary>
    public Dictionary<string, object?> Output { get; set; } = new();
    
    /// <summary>Error if activity failed.</summary>
    public WorkflowError? Error { get; set; }
    
    /// <summary>Attempt number (1-based).</summary>
    public int Attempt { get; set; } = 1;
    
    /// <summary>When execution started.</summary>
    public DateTimeOffset StartedAt { get; set; }
    
    /// <summary>When execution completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
    
    /// <summary>Duration in milliseconds.</summary>
    public long? DurationMs { get; set; }
    
    /// <summary>Worker that processed this activity.</summary>
    public string? WorkerId { get; set; }
}
