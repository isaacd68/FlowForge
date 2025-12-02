using FlowForge.Shared.Constants;

namespace FlowForge.Shared.DTOs;

// ============== Workflow DTOs ==============

/// <summary>
/// Request to start a new workflow instance.
/// </summary>
public class StartWorkflowRequest
{
    /// <summary>Name of the workflow to start.</summary>
    public required string WorkflowName { get; set; }
    
    /// <summary>Optional specific version (defaults to latest).</summary>
    public int? Version { get; set; }
    
    /// <summary>Input data for the workflow.</summary>
    public Dictionary<string, object?>? Input { get; set; }
    
    /// <summary>Correlation ID for tracking.</summary>
    public string? CorrelationId { get; set; }
    
    /// <summary>Tags to apply to the instance.</summary>
    public List<string>? Tags { get; set; }
    
    /// <summary>Custom metadata.</summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Response when a workflow is started.
/// </summary>
public class StartWorkflowResponse
{
    /// <summary>ID of the created workflow instance.</summary>
    public required Guid InstanceId { get; set; }
    
    /// <summary>Current status.</summary>
    public WorkflowStatus Status { get; set; }
    
    /// <summary>When the workflow was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Summary view of a workflow instance.
/// </summary>
public class WorkflowInstanceSummary
{
    public Guid Id { get; set; }
    public required string WorkflowName { get; set; }
    public int WorkflowVersion { get; set; }
    public WorkflowStatus Status { get; set; }
    public string? CurrentActivityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Detailed view of a workflow instance.
/// </summary>
public class WorkflowInstanceDetail
{
    public Guid Id { get; set; }
    public required string WorkflowName { get; set; }
    public int WorkflowVersion { get; set; }
    public WorkflowStatus Status { get; set; }
    public Dictionary<string, object?> Input { get; set; } = new();
    public Dictionary<string, object?> Output { get; set; } = new();
    public Dictionary<string, object?> State { get; set; } = new();
    public string? CurrentActivityId { get; set; }
    public Guid? ParentInstanceId { get; set; }
    public string? CorrelationId { get; set; }
    public WorkflowErrorDto? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int RetryCount { get; set; }
    public string? WorkerId { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public List<ActivityExecutionDto> ActivityHistory { get; set; } = new();
}

/// <summary>
/// Error details DTO.
/// </summary>
public class WorkflowErrorDto
{
    public required string Code { get; set; }
    public required string Message { get; set; }
    public string? ActivityId { get; set; }
    public string? StackTrace { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// Activity execution history DTO.
/// </summary>
public class ActivityExecutionDto
{
    public Guid Id { get; set; }
    public required string ActivityId { get; set; }
    public required string ActivityType { get; set; }
    public ActivityStatus Status { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public WorkflowErrorDto? Error { get; set; }
}

// ============== Definition DTOs ==============

/// <summary>
/// Request to create or update a workflow definition.
/// </summary>
public class CreateWorkflowDefinitionRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<ActivityDefinitionDto> Activities { get; set; } = new();
    public List<TransitionDefinitionDto> Transitions { get; set; } = new();
    public required string StartActivityId { get; set; }
    public InputSchemaDto? InputSchema { get; set; }
    public OutputSchemaDto? OutputSchema { get; set; }
    public TriggerDefinitionDto? Trigger { get; set; }
    public RetryPolicyDto? DefaultRetryPolicy { get; set; }
    public int? TimeoutSeconds { get; set; }
    public List<string>? Tags { get; set; }
}

/// <summary>
/// Activity definition DTO.
/// </summary>
public class ActivityDefinitionDto
{
    public required string Id { get; set; }
    public required string Type { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, object?> Properties { get; set; } = new();
    public Dictionary<string, string> InputMappings { get; set; } = new();
    public Dictionary<string, string> OutputMappings { get; set; } = new();
    public RetryPolicyDto? RetryPolicy { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? Condition { get; set; }
}

/// <summary>
/// Transition definition DTO.
/// </summary>
public class TransitionDefinitionDto
{
    public required string From { get; set; }
    public required string To { get; set; }
    public string? Condition { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsDefault { get; set; }
}

/// <summary>
/// Trigger definition DTO.
/// </summary>
public class TriggerDefinitionDto
{
    public TriggerType Type { get; set; }
    public string? CronExpression { get; set; }
    public string? EventType { get; set; }
    public string? EventFilter { get; set; }
}

/// <summary>
/// Retry policy DTO.
/// </summary>
public class RetryPolicyDto
{
    public int MaxAttempts { get; set; } = 3;
    public int InitialDelaySeconds { get; set; } = 1;
    public int MaxDelaySeconds { get; set; } = 300;
    public double BackoffMultiplier { get; set; } = 2.0;
}

/// <summary>
/// Input schema DTO.
/// </summary>
public class InputSchemaDto
{
    public Dictionary<string, PropertySchemaDto> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

/// <summary>
/// Output schema DTO.
/// </summary>
public class OutputSchemaDto
{
    public Dictionary<string, PropertySchemaDto> Properties { get; set; } = new();
}

/// <summary>
/// Property schema DTO.
/// </summary>
public class PropertySchemaDto
{
    public required string Type { get; set; }
    public string? Description { get; set; }
    public object? Default { get; set; }
}

/// <summary>
/// Workflow definition summary.
/// </summary>
public class WorkflowDefinitionSummary
{
    public required string Name { get; set; }
    public int Version { get; set; }
    public string? Description { get; set; }
    public int ActivityCount { get; set; }
    public bool IsActive { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// ============== Query/Filter DTOs ==============

/// <summary>
/// Query parameters for listing workflow instances.
/// </summary>
public class WorkflowInstanceQuery
{
    public string? WorkflowName { get; set; }
    public WorkflowStatus? Status { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset? CreatedAfter { get; set; }
    public DateTimeOffset? CreatedBefore { get; set; }
    public List<string>? Tags { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// Paginated result wrapper.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

// ============== Dashboard/Stats DTOs ==============

/// <summary>
/// Dashboard statistics.
/// </summary>
public class DashboardStats
{
    public int TotalWorkflows { get; set; }
    public int ActiveInstances { get; set; }
    public int CompletedToday { get; set; }
    public int FailedToday { get; set; }
    public double SuccessRate { get; set; }
    public double AverageDurationMs { get; set; }
    public List<WorkflowStats> WorkflowBreakdown { get; set; } = new();
    public List<HourlyStats> Last24Hours { get; set; } = new();
}

/// <summary>
/// Per-workflow statistics.
/// </summary>
public class WorkflowStats
{
    public required string WorkflowName { get; set; }
    public int TotalInstances { get; set; }
    public int Running { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public double AvgDurationMs { get; set; }
}

/// <summary>
/// Hourly execution statistics.
/// </summary>
public class HourlyStats
{
    public DateTimeOffset Hour { get; set; }
    public int Started { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
}

// ============== Worker DTOs ==============

/// <summary>
/// Worker node information.
/// </summary>
public class WorkerInfo
{
    public required string WorkerId { get; set; }
    public required string Hostname { get; set; }
    public WorkerStatus Status { get; set; }
    public int CurrentJobs { get; set; }
    public int MaxConcurrency { get; set; }
    public List<string> SupportedActivities { get; set; } = new();
    public DateTimeOffset LastHeartbeat { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public WorkerMetrics? Metrics { get; set; }
}

/// <summary>
/// Worker performance metrics.
/// </summary>
public class WorkerMetrics
{
    public double CpuUsage { get; set; }
    public double MemoryUsageMb { get; set; }
    public int JobsProcessed { get; set; }
    public int JobsFailed { get; set; }
    public double AvgProcessingTimeMs { get; set; }
}
