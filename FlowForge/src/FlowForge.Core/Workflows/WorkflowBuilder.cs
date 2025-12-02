using FlowForge.Shared.Constants;
using FlowForge.Shared.Models;

namespace FlowForge.Core.Workflows;

/// <summary>
/// Fluent builder for creating workflow definitions.
/// </summary>
public class WorkflowBuilder
{
    private readonly WorkflowDefinition _definition;
    private ActivityDefinition? _currentActivity;

    private WorkflowBuilder(string name)
    {
        _definition = new WorkflowDefinition
        {
            Name = name,
            StartActivityId = string.Empty
        };
    }

    /// <summary>
    /// Create a new workflow builder.
    /// </summary>
    public static WorkflowBuilder Create(string name) => new(name);

    /// <summary>
    /// Set workflow description.
    /// </summary>
    public WorkflowBuilder WithDescription(string description)
    {
        _definition.Description = description;
        return this;
    }

    /// <summary>
    /// Set workflow version.
    /// </summary>
    public WorkflowBuilder WithVersion(int version)
    {
        _definition.Version = version;
        return this;
    }

    /// <summary>
    /// Add tags to the workflow.
    /// </summary>
    public WorkflowBuilder WithTags(params string[] tags)
    {
        _definition.Tags.AddRange(tags);
        return this;
    }

    /// <summary>
    /// Set workflow timeout.
    /// </summary>
    public WorkflowBuilder WithTimeout(TimeSpan timeout)
    {
        _definition.Timeout = timeout;
        return this;
    }

    /// <summary>
    /// Configure default retry policy.
    /// </summary>
    public WorkflowBuilder WithDefaultRetryPolicy(Action<RetryPolicyBuilder> configure)
    {
        var builder = new RetryPolicyBuilder();
        configure(builder);
        _definition.DefaultRetryPolicy = builder.Build();
        return this;
    }

    /// <summary>
    /// Define input schema.
    /// </summary>
    public WorkflowBuilder WithInput(Action<InputSchemaBuilder> configure)
    {
        var builder = new InputSchemaBuilder();
        configure(builder);
        _definition.InputSchema = builder.Build();
        return this;
    }

    /// <summary>
    /// Define output schema.
    /// </summary>
    public WorkflowBuilder WithOutput(Action<OutputSchemaBuilder> configure)
    {
        var builder = new OutputSchemaBuilder();
        configure(builder);
        _definition.OutputSchema = builder.Build();
        return this;
    }

    /// <summary>
    /// Configure a scheduled trigger.
    /// </summary>
    public WorkflowBuilder WithScheduledTrigger(string cronExpression)
    {
        _definition.Trigger = new TriggerDefinition
        {
            Type = TriggerType.Scheduled,
            CronExpression = cronExpression
        };
        return this;
    }

    /// <summary>
    /// Configure an event trigger.
    /// </summary>
    public WorkflowBuilder WithEventTrigger(string eventType, string? filter = null)
    {
        _definition.Trigger = new TriggerDefinition
        {
            Type = TriggerType.Event,
            EventType = eventType,
            EventFilter = filter
        };
        return this;
    }

    /// <summary>
    /// Configure a webhook trigger.
    /// </summary>
    public WorkflowBuilder WithWebhookTrigger(string? secret = null, string? inputTransform = null)
    {
        _definition.Trigger = new TriggerDefinition
        {
            Type = TriggerType.Webhook,
            Webhook = new WebhookConfig
            {
                Secret = secret,
                InputTransform = inputTransform
            }
        };
        return this;
    }

    /// <summary>
    /// Add an activity to the workflow.
    /// </summary>
    public WorkflowBuilder AddActivity(string id, string type, Action<ActivityBuilder>? configure = null)
    {
        var activity = new ActivityDefinition
        {
            Id = id,
            Type = type
        };

        if (configure is not null)
        {
            var builder = new ActivityBuilder(activity);
            configure(builder);
        }

        _definition.Activities.Add(activity);
        _currentActivity = activity;

        // Set as start activity if first
        if (_definition.Activities.Count == 1)
        {
            _definition.StartActivityId = id;
        }

        return this;
    }

    /// <summary>
    /// Add a log activity.
    /// </summary>
    public WorkflowBuilder AddLog(string id, string message, string level = "Information")
    {
        return AddActivity(id, "log", a => a
            .WithProperty("message", message)
            .WithProperty("level", level));
    }

    /// <summary>
    /// Add a delay activity.
    /// </summary>
    public WorkflowBuilder AddDelay(string id, int delayMs)
    {
        return AddActivity(id, "delay", a => a.WithProperty("delayMs", delayMs));
    }

    /// <summary>
    /// Add an HTTP activity.
    /// </summary>
    public WorkflowBuilder AddHttp(string id, string url, string method = "GET", Action<ActivityBuilder>? configure = null)
    {
        return AddActivity(id, "http", a =>
        {
            a.WithProperty("url", url).WithProperty("method", method);
            configure?.Invoke(a);
        });
    }

    /// <summary>
    /// Add a transform activity.
    /// </summary>
    public WorkflowBuilder AddTransform(string id, Dictionary<string, string> mappings)
    {
        return AddActivity(id, "transform", a => a.WithProperty("mappings", mappings));
    }

    /// <summary>
    /// Add a condition activity.
    /// </summary>
    public WorkflowBuilder AddCondition(string id, Action<ConditionBuilder> configure)
    {
        var builder = new ConditionBuilder();
        configure(builder);
        
        return AddActivity(id, "condition", a =>
        {
            a.WithProperty("conditions", builder.Branches);
            if (builder.DefaultActivityId is not null)
            {
                a.WithProperty("defaultNextActivity", builder.DefaultActivityId);
            }
        });
    }

    /// <summary>
    /// Add a wait for signal activity.
    /// </summary>
    public WorkflowBuilder AddWaitForSignal(string id, string signalName, int? timeoutSeconds = null)
    {
        return AddActivity(id, "waitForSignal", a =>
        {
            a.WithProperty("signalName", signalName);
            if (timeoutSeconds.HasValue)
            {
                a.WithProperty("timeoutSeconds", timeoutSeconds.Value);
            }
        });
    }

    /// <summary>
    /// Add a child workflow invocation.
    /// </summary>
    public WorkflowBuilder AddInvokeWorkflow(string id, string workflowName, Dictionary<string, object?>? input = null, bool waitForCompletion = true)
    {
        return AddActivity(id, "invokeWorkflow", a =>
        {
            a.WithProperty("workflowName", workflowName);
            a.WithProperty("waitForCompletion", waitForCompletion);
            if (input is not null)
            {
                a.WithProperty("input", input);
            }
        });
    }

    /// <summary>
    /// Add a state setter activity.
    /// </summary>
    public WorkflowBuilder AddSetState(string id, Dictionary<string, object?> values)
    {
        return AddActivity(id, "setState", a => a.WithProperty("values", values));
    }

    /// <summary>
    /// Set the start activity.
    /// </summary>
    public WorkflowBuilder StartsWith(string activityId)
    {
        _definition.StartActivityId = activityId;
        return this;
    }

    /// <summary>
    /// Add a transition from current activity.
    /// </summary>
    public WorkflowBuilder Then(string toActivityId, string? condition = null)
    {
        if (_currentActivity is null)
        {
            throw new InvalidOperationException("No current activity to transition from. Add an activity first.");
        }

        _definition.Transitions.Add(new TransitionDefinition
        {
            From = _currentActivity.Id,
            To = toActivityId,
            Condition = condition
        });

        return this;
    }

    /// <summary>
    /// Add a conditional transition.
    /// </summary>
    public WorkflowBuilder ThenIf(string condition, string toActivityId, int priority = 100)
    {
        if (_currentActivity is null)
        {
            throw new InvalidOperationException("No current activity to transition from.");
        }

        _definition.Transitions.Add(new TransitionDefinition
        {
            From = _currentActivity.Id,
            To = toActivityId,
            Condition = condition,
            Priority = priority
        });

        return this;
    }

    /// <summary>
    /// Add a default transition (when no conditions match).
    /// </summary>
    public WorkflowBuilder ThenDefault(string toActivityId)
    {
        if (_currentActivity is null)
        {
            throw new InvalidOperationException("No current activity to transition from.");
        }

        _definition.Transitions.Add(new TransitionDefinition
        {
            From = _currentActivity.Id,
            To = toActivityId,
            IsDefault = true,
            Priority = int.MaxValue
        });

        return this;
    }

    /// <summary>
    /// Add a transition between specific activities.
    /// </summary>
    public WorkflowBuilder AddTransition(string from, string to, string? condition = null, bool isDefault = false)
    {
        _definition.Transitions.Add(new TransitionDefinition
        {
            From = from,
            To = to,
            Condition = condition,
            IsDefault = isDefault
        });
        return this;
    }

    /// <summary>
    /// Build the workflow definition.
    /// </summary>
    public WorkflowDefinition Build()
    {
        if (string.IsNullOrEmpty(_definition.StartActivityId))
        {
            throw new InvalidOperationException("Workflow must have a start activity");
        }

        if (_definition.Activities.Count == 0)
        {
            throw new InvalidOperationException("Workflow must have at least one activity");
        }

        // Validate all transitions reference valid activities
        var activityIds = _definition.Activities.Select(a => a.Id).ToHashSet();
        
        if (!activityIds.Contains(_definition.StartActivityId))
        {
            throw new InvalidOperationException($"Start activity '{_definition.StartActivityId}' not found");
        }

        foreach (var transition in _definition.Transitions)
        {
            if (!activityIds.Contains(transition.From))
            {
                throw new InvalidOperationException($"Transition 'from' activity '{transition.From}' not found");
            }
            if (!activityIds.Contains(transition.To))
            {
                throw new InvalidOperationException($"Transition 'to' activity '{transition.To}' not found");
            }
        }

        return _definition;
    }
}

/// <summary>
/// Builder for activity configuration.
/// </summary>
public class ActivityBuilder
{
    private readonly ActivityDefinition _activity;

    internal ActivityBuilder(ActivityDefinition activity) => _activity = activity;

    public ActivityBuilder WithName(string name)
    {
        _activity.Name = name;
        return this;
    }

    public ActivityBuilder WithDescription(string description)
    {
        _activity.Description = description;
        return this;
    }

    public ActivityBuilder WithProperty(string name, object? value)
    {
        _activity.Properties[name] = value;
        return this;
    }

    public ActivityBuilder WithInputMapping(string inputKey, string expression)
    {
        _activity.InputMappings[inputKey] = expression;
        return this;
    }

    public ActivityBuilder WithOutputMapping(string stateKey, string outputKey)
    {
        _activity.OutputMappings[stateKey] = outputKey;
        return this;
    }

    public ActivityBuilder WithCondition(string condition)
    {
        _activity.Condition = condition;
        return this;
    }

    public ActivityBuilder WithTimeout(TimeSpan timeout)
    {
        _activity.Timeout = timeout;
        return this;
    }

    public ActivityBuilder WithRetryPolicy(Action<RetryPolicyBuilder> configure)
    {
        var builder = new RetryPolicyBuilder();
        configure(builder);
        _activity.RetryPolicy = builder.Build();
        return this;
    }
}

/// <summary>
/// Builder for retry policy configuration.
/// </summary>
public class RetryPolicyBuilder
{
    private readonly RetryPolicy _policy = new();

    public RetryPolicyBuilder MaxAttempts(int attempts)
    {
        _policy.MaxAttempts = attempts;
        return this;
    }

    public RetryPolicyBuilder InitialDelay(TimeSpan delay)
    {
        _policy.InitialDelay = delay;
        return this;
    }

    public RetryPolicyBuilder MaxDelay(TimeSpan delay)
    {
        _policy.MaxDelay = delay;
        return this;
    }

    public RetryPolicyBuilder BackoffMultiplier(double multiplier)
    {
        _policy.BackoffMultiplier = multiplier;
        return this;
    }

    public RetryPolicyBuilder RetryOn(params string[] exceptionTypes)
    {
        _policy.RetryOn.AddRange(exceptionTypes);
        return this;
    }

    public RetryPolicyBuilder DoNotRetryOn(params string[] exceptionTypes)
    {
        _policy.DoNotRetryOn.AddRange(exceptionTypes);
        return this;
    }

    internal RetryPolicy Build() => _policy;
}

/// <summary>
/// Builder for input schema.
/// </summary>
public class InputSchemaBuilder
{
    private readonly InputSchema _schema = new();

    public InputSchemaBuilder AddProperty(string name, string type, bool required = false, string? description = null, object? defaultValue = null)
    {
        _schema.Properties[name] = new PropertySchema
        {
            Type = type,
            Description = description,
            Default = defaultValue
        };

        if (required)
        {
            _schema.Required.Add(name);
        }

        return this;
    }

    public InputSchemaBuilder AddString(string name, bool required = false, string? description = null) =>
        AddProperty(name, "string", required, description);

    public InputSchemaBuilder AddNumber(string name, bool required = false, string? description = null) =>
        AddProperty(name, "number", required, description);

    public InputSchemaBuilder AddBoolean(string name, bool required = false, string? description = null) =>
        AddProperty(name, "boolean", required, description);

    public InputSchemaBuilder AddObject(string name, bool required = false, string? description = null) =>
        AddProperty(name, "object", required, description);

    public InputSchemaBuilder AddArray(string name, bool required = false, string? description = null) =>
        AddProperty(name, "array", required, description);

    internal InputSchema Build() => _schema;
}

/// <summary>
/// Builder for output schema.
/// </summary>
public class OutputSchemaBuilder
{
    private readonly OutputSchema _schema = new();

    public OutputSchemaBuilder AddProperty(string name, string type, string? description = null)
    {
        _schema.Properties[name] = new PropertySchema
        {
            Type = type,
            Description = description
        };
        return this;
    }

    internal OutputSchema Build() => _schema;
}

/// <summary>
/// Builder for condition activity.
/// </summary>
public class ConditionBuilder
{
    internal List<ConditionBranch> Branches { get; } = new();
    internal string? DefaultActivityId { get; private set; }

    public ConditionBuilder When(string expression, string name, string nextActivityId)
    {
        Branches.Add(new ConditionBranch
        {
            Name = name,
            Expression = expression,
            NextActivityId = nextActivityId
        });
        return this;
    }

    public ConditionBuilder Default(string nextActivityId)
    {
        DefaultActivityId = nextActivityId;
        return this;
    }
}

/// <summary>
/// Condition branch for condition activity.
/// </summary>
public class ConditionBranch
{
    public required string Name { get; set; }
    public required string Expression { get; set; }
    public required string NextActivityId { get; set; }
}
