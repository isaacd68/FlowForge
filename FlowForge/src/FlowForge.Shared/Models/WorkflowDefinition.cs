using FlowForge.Shared.Constants;

namespace FlowForge.Shared.Models;

/// <summary>
/// Represents a workflow definition that can be executed.
/// </summary>
public class WorkflowDefinition
{
    /// <summary>Unique name identifier for the workflow.</summary>
    public required string Name { get; set; }
    
    /// <summary>Version number of this definition.</summary>
    public int Version { get; set; } = 1;
    
    /// <summary>Human-readable description.</summary>
    public string? Description { get; set; }
    
    /// <summary>Activities that make up this workflow.</summary>
    public List<ActivityDefinition> Activities { get; set; } = new();
    
    /// <summary>Transitions between activities.</summary>
    public List<TransitionDefinition> Transitions { get; set; } = new();
    
    /// <summary>ID of the starting activity.</summary>
    public required string StartActivityId { get; set; }
    
    /// <summary>Input schema for validation.</summary>
    public InputSchema? InputSchema { get; set; }
    
    /// <summary>Output schema for validation.</summary>
    public OutputSchema? OutputSchema { get; set; }
    
    /// <summary>Trigger configuration for automatic execution.</summary>
    public TriggerDefinition? Trigger { get; set; }
    
    /// <summary>Default retry policy for activities.</summary>
    public RetryPolicy? DefaultRetryPolicy { get; set; }
    
    /// <summary>Maximum execution time for the entire workflow.</summary>
    public TimeSpan? Timeout { get; set; }
    
    /// <summary>Whether this workflow definition is active.</summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>Tags for categorization.</summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>When this definition was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>When this definition was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Defines an activity within a workflow.
/// </summary>
public class ActivityDefinition
{
    /// <summary>Unique identifier within the workflow.</summary>
    public required string Id { get; set; }
    
    /// <summary>Type of activity to execute.</summary>
    public required string Type { get; set; }
    
    /// <summary>Human-readable name.</summary>
    public string? Name { get; set; }
    
    /// <summary>Description of what this activity does.</summary>
    public string? Description { get; set; }
    
    /// <summary>Configuration properties for the activity.</summary>
    public Dictionary<string, object?> Properties { get; set; } = new();
    
    /// <summary>Input mappings from workflow state to activity input.</summary>
    public Dictionary<string, string> InputMappings { get; set; } = new();
    
    /// <summary>Output mappings from activity output to workflow state.</summary>
    public Dictionary<string, string> OutputMappings { get; set; } = new();
    
    /// <summary>Retry policy specific to this activity.</summary>
    public RetryPolicy? RetryPolicy { get; set; }
    
    /// <summary>Timeout for this activity.</summary>
    public TimeSpan? Timeout { get; set; }
    
    /// <summary>Condition expression for whether to execute this activity.</summary>
    public string? Condition { get; set; }
}

/// <summary>
/// Defines a transition between activities.
/// </summary>
public class TransitionDefinition
{
    /// <summary>Source activity ID.</summary>
    public required string From { get; set; }
    
    /// <summary>Target activity ID.</summary>
    public required string To { get; set; }
    
    /// <summary>Condition expression for this transition.</summary>
    public string? Condition { get; set; }
    
    /// <summary>Priority when multiple transitions are possible (lower = higher priority).</summary>
    public int Priority { get; set; } = 100;
    
    /// <summary>Whether this is the default transition when no conditions match.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Defines how a workflow can be triggered.
/// </summary>
public class TriggerDefinition
{
    /// <summary>Type of trigger.</summary>
    public TriggerType Type { get; set; }
    
    /// <summary>Cron expression for scheduled triggers.</summary>
    public string? CronExpression { get; set; }
    
    /// <summary>Event type for event-based triggers.</summary>
    public string? EventType { get; set; }
    
    /// <summary>Filter expression for event triggers.</summary>
    public string? EventFilter { get; set; }
    
    /// <summary>Webhook configuration.</summary>
    public WebhookConfig? Webhook { get; set; }
}

/// <summary>
/// Webhook trigger configuration.
/// </summary>
public class WebhookConfig
{
    /// <summary>Secret for validating webhook calls.</summary>
    public string? Secret { get; set; }
    
    /// <summary>Allowed HTTP methods.</summary>
    public List<string> Methods { get; set; } = new() { "POST" };
    
    /// <summary>Input transformation expression.</summary>
    public string? InputTransform { get; set; }
}

/// <summary>
/// Retry policy configuration.
/// </summary>
public class RetryPolicy
{
    /// <summary>Maximum number of retry attempts.</summary>
    public int MaxAttempts { get; set; } = 3;
    
    /// <summary>Initial delay between retries.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    
    /// <summary>Maximum delay between retries.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
    
    /// <summary>Multiplier for exponential backoff.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;
    
    /// <summary>Exception types that should trigger a retry.</summary>
    public List<string> RetryOn { get; set; } = new();
    
    /// <summary>Exception types that should not trigger a retry.</summary>
    public List<string> DoNotRetryOn { get; set; } = new();
}

/// <summary>
/// Input schema definition for workflow validation.
/// </summary>
public class InputSchema
{
    /// <summary>Schema properties.</summary>
    public Dictionary<string, PropertySchema> Properties { get; set; } = new();
    
    /// <summary>Required property names.</summary>
    public List<string> Required { get; set; } = new();
}

/// <summary>
/// Output schema definition.
/// </summary>
public class OutputSchema
{
    /// <summary>Schema properties.</summary>
    public Dictionary<string, PropertySchema> Properties { get; set; } = new();
}

/// <summary>
/// Property schema for input/output validation.
/// </summary>
public class PropertySchema
{
    /// <summary>Property type (string, number, boolean, object, array).</summary>
    public required string Type { get; set; }
    
    /// <summary>Property description.</summary>
    public string? Description { get; set; }
    
    /// <summary>Default value.</summary>
    public object? Default { get; set; }
    
    /// <summary>Enum values if restricted.</summary>
    public List<object>? Enum { get; set; }
}
