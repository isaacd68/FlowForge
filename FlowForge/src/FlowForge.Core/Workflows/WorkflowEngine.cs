using FlowForge.Core.Activities;
using FlowForge.Shared.Constants;
using FlowForge.Shared.Contracts;
using FlowForge.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Workflows;

/// <summary>
/// Options for the workflow engine.
/// </summary>
public class WorkflowEngineOptions
{
    /// <summary>Default timeout for workflows.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromHours(1);
    
    /// <summary>Default retry policy.</summary>
    public RetryPolicy DefaultRetryPolicy { get; set; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromMinutes(5),
        BackoffMultiplier = 2.0
    };
    
    /// <summary>Maximum concurrent activities per workflow.</summary>
    public int MaxParallelActivities { get; set; } = 10;
    
    /// <summary>Enable detailed execution logging.</summary>
    public bool EnableDetailedLogging { get; set; } = true;
}

/// <summary>
/// Result of a workflow engine operation.
/// </summary>
public class WorkflowEngineResult
{
    public bool Success { get; init; }
    public WorkflowInstance? Instance { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    
    public static WorkflowEngineResult Ok(WorkflowInstance instance) => new()
    {
        Success = true,
        Instance = instance
    };
    
    public static WorkflowEngineResult Fail(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}

/// <summary>
/// Core workflow execution engine.
/// </summary>
public class WorkflowEngine
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowInstanceRepository _instanceRepository;
    private readonly IActivityExecutionRepository _executionRepository;
    private readonly IDistributedLockService _lockService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkflowEngine> _logger;
    private readonly WorkflowEngineOptions _options;
    private readonly Dictionary<string, IActivity> _activities;

    public WorkflowEngine(
        IWorkflowDefinitionRepository definitionRepository,
        IWorkflowInstanceRepository instanceRepository,
        IActivityExecutionRepository executionRepository,
        IDistributedLockService lockService,
        IServiceProvider serviceProvider,
        ILogger<WorkflowEngine> logger,
        WorkflowEngineOptions options)
    {
        _definitionRepository = definitionRepository;
        _instanceRepository = instanceRepository;
        _executionRepository = executionRepository;
        _lockService = lockService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
        _activities = new Dictionary<string, IActivity>(StringComparer.OrdinalIgnoreCase);
        
        RegisterBuiltInActivities();
    }

    /// <summary>
    /// Register a custom activity type.
    /// </summary>
    public void RegisterActivity(IActivity activity)
    {
        _activities[activity.Type] = activity;
        _logger.LogInformation("Registered activity type: {ActivityType}", activity.Type);
    }

    /// <summary>
    /// Start a new workflow instance.
    /// </summary>
    public async Task<WorkflowEngineResult> StartWorkflowAsync(
        string workflowName,
        Dictionary<string, object?>? input = null,
        string? correlationId = null,
        Guid? parentInstanceId = null,
        CancellationToken ct = default)
    {
        var definition = await _definitionRepository.GetAsync(workflowName, ct: ct);
        if (definition is null)
        {
            return WorkflowEngineResult.Fail("WORKFLOW_NOT_FOUND", $"Workflow '{workflowName}' not found");
        }

        if (!definition.IsActive)
        {
            return WorkflowEngineResult.Fail("WORKFLOW_INACTIVE", $"Workflow '{workflowName}' is not active");
        }

        // Validate input
        if (definition.InputSchema is not null)
        {
            var validationError = ValidateInput(input, definition.InputSchema);
            if (validationError is not null)
            {
                return WorkflowEngineResult.Fail("INVALID_INPUT", validationError);
            }
        }

        var instance = new WorkflowInstance
        {
            WorkflowName = workflowName,
            WorkflowVersion = definition.Version,
            Status = WorkflowStatus.Pending,
            Input = input ?? new(),
            CorrelationId = correlationId,
            ParentInstanceId = parentInstanceId,
            CurrentActivityId = definition.StartActivityId
        };

        instance = await _instanceRepository.CreateAsync(instance, ct);
        
        _logger.LogInformation(
            "Created workflow instance {InstanceId} for workflow {WorkflowName} v{Version}",
            instance.Id, workflowName, definition.Version);

        return WorkflowEngineResult.Ok(instance);
    }

    /// <summary>
    /// Execute a workflow instance.
    /// </summary>
    public async Task<WorkflowEngineResult> ExecuteAsync(Guid instanceId, CancellationToken ct = default)
    {
        await using var lockHandle = await _lockService.AcquireLockAsync(instanceId, TimeSpan.FromMinutes(5), ct);
        if (lockHandle is null)
        {
            return WorkflowEngineResult.Fail("LOCK_FAILED", "Could not acquire lock for workflow instance");
        }

        var instance = await _instanceRepository.GetAsync(instanceId, ct);
        if (instance is null)
        {
            return WorkflowEngineResult.Fail("INSTANCE_NOT_FOUND", $"Workflow instance {instanceId} not found");
        }

        if (instance.Status is WorkflowStatus.Completed or WorkflowStatus.Failed or WorkflowStatus.Cancelled)
        {
            return WorkflowEngineResult.Ok(instance);
        }

        var definition = await _definitionRepository.GetAsync(instance.WorkflowName, instance.WorkflowVersion, ct);
        if (definition is null)
        {
            return WorkflowEngineResult.Fail("DEFINITION_NOT_FOUND", 
                $"Workflow definition {instance.WorkflowName} v{instance.WorkflowVersion} not found");
        }

        // Update status to running
        if (instance.Status == WorkflowStatus.Pending)
        {
            instance.Status = WorkflowStatus.Running;
            instance.StartedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            // Execute activities until completion, suspension, or failure
            while (instance.Status == WorkflowStatus.Running && instance.CurrentActivityId is not null)
            {
                ct.ThrowIfCancellationRequested();

                var activityDef = definition.Activities.FirstOrDefault(a => a.Id == instance.CurrentActivityId);
                if (activityDef is null)
                {
                    return await FailWorkflowAsync(instance, "ACTIVITY_NOT_FOUND",
                        $"Activity '{instance.CurrentActivityId}' not found in definition", ct);
                }

                // Check condition
                if (!string.IsNullOrEmpty(activityDef.Condition))
                {
                    if (!EvaluateCondition(activityDef.Condition, instance))
                    {
                        _logger.LogDebug("Skipping activity {ActivityId} - condition not met", activityDef.Id);
                        instance.CurrentActivityId = GetNextActivityId(definition, activityDef.Id, instance);
                        continue;
                    }
                }

                var result = await ExecuteActivityAsync(instance, definition, activityDef, ct);

                if (!result.Success)
                {
                    // Check if we should retry
                    var retryPolicy = activityDef.RetryPolicy ?? definition.DefaultRetryPolicy ?? _options.DefaultRetryPolicy;
                    if (instance.RetryCount < retryPolicy.MaxAttempts && (result.Error?.Retriable ?? true))
                    {
                        instance.RetryCount++;
                        var delay = CalculateRetryDelay(instance.RetryCount, retryPolicy);
                        
                        _logger.LogWarning(
                            "Activity {ActivityId} failed, retry {RetryCount}/{MaxAttempts} after {Delay}ms",
                            activityDef.Id, instance.RetryCount, retryPolicy.MaxAttempts, delay.TotalMilliseconds);
                        
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    return await FailWorkflowAsync(instance, result.Error?.Code ?? "ACTIVITY_FAILED",
                        result.Error?.Message ?? "Activity failed", ct, activityDef.Id);
                }

                // Activity succeeded
                instance.RetryCount = 0;

                if (result.Suspend)
                {
                    instance.Status = WorkflowStatus.Suspended;
                    instance.State["_suspendKey"] = result.SuspendKey;
                    _logger.LogInformation(
                        "Workflow {InstanceId} suspended at activity {ActivityId} waiting for signal {SuspendKey}",
                        instance.Id, activityDef.Id, result.SuspendKey);
                    break;
                }

                // Apply output mappings
                foreach (var (stateKey, outputKey) in activityDef.OutputMappings)
                {
                    if (result.Output.TryGetValue(outputKey, out var value))
                    {
                        instance.State[stateKey] = value;
                    }
                }

                // Determine next activity
                var nextActivityId = result.NextActivityId ?? GetNextActivityId(definition, activityDef.Id, instance);

                if (nextActivityId is null)
                {
                    // Workflow completed
                    instance.Status = WorkflowStatus.Completed;
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    instance.Output = BuildOutput(definition, instance);
                    
                    _logger.LogInformation(
                        "Workflow {InstanceId} completed successfully in {Duration}ms",
                        instance.Id, (instance.CompletedAt - instance.StartedAt)?.TotalMilliseconds);
                }
                else
                {
                    instance.CurrentActivityId = nextActivityId;
                }
            }

            instance.UpdatedAt = DateTimeOffset.UtcNow;
            await _instanceRepository.UpdateAsync(instance, ct);
            
            return WorkflowEngineResult.Ok(instance);
        }
        catch (OperationCanceledException)
        {
            instance.Status = WorkflowStatus.Cancelled;
            instance.CompletedAt = DateTimeOffset.UtcNow;
            await _instanceRepository.UpdateAsync(instance, ct);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing workflow {InstanceId}", instanceId);
            return await FailWorkflowAsync(instance, "UNEXPECTED_ERROR", ex.Message, ct);
        }
    }

    /// <summary>
    /// Resume a suspended workflow with a signal.
    /// </summary>
    public async Task<WorkflowEngineResult> ResumeWithSignalAsync(
        Guid instanceId,
        string signalName,
        Dictionary<string, object?>? data = null,
        CancellationToken ct = default)
    {
        var instance = await _instanceRepository.GetAsync(instanceId, ct);
        if (instance is null)
        {
            return WorkflowEngineResult.Fail("INSTANCE_NOT_FOUND", $"Workflow instance {instanceId} not found");
        }

        if (instance.Status != WorkflowStatus.Suspended)
        {
            return WorkflowEngineResult.Fail("NOT_SUSPENDED", "Workflow is not in suspended state");
        }

        var expectedSignal = instance.State.GetValueOrDefault("_suspendKey")?.ToString();
        if (expectedSignal != signalName)
        {
            return WorkflowEngineResult.Fail("SIGNAL_MISMATCH", 
                $"Expected signal '{expectedSignal}', received '{signalName}'");
        }

        // Apply signal data to state
        if (data is not null)
        {
            foreach (var (key, value) in data)
            {
                instance.State[$"signal_{key}"] = value;
            }
        }

        instance.State.Remove("_suspendKey");
        instance.Status = WorkflowStatus.Running;
        
        // Get next activity
        var definition = await _definitionRepository.GetAsync(instance.WorkflowName, instance.WorkflowVersion, ct);
        if (definition is not null)
        {
            instance.CurrentActivityId = GetNextActivityId(definition, instance.CurrentActivityId!, instance);
        }

        await _instanceRepository.UpdateAsync(instance, ct);

        return await ExecuteAsync(instanceId, ct);
    }

    /// <summary>
    /// Cancel a workflow instance.
    /// </summary>
    public async Task<WorkflowEngineResult> CancelAsync(Guid instanceId, CancellationToken ct = default)
    {
        var instance = await _instanceRepository.GetAsync(instanceId, ct);
        if (instance is null)
        {
            return WorkflowEngineResult.Fail("INSTANCE_NOT_FOUND", $"Workflow instance {instanceId} not found");
        }

        if (instance.Status is WorkflowStatus.Completed or WorkflowStatus.Failed or WorkflowStatus.Cancelled)
        {
            return WorkflowEngineResult.Ok(instance);
        }

        instance.Status = WorkflowStatus.Cancelled;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        await _instanceRepository.UpdateAsync(instance, ct);
        
        _logger.LogInformation("Workflow {InstanceId} was cancelled", instanceId);
        
        return WorkflowEngineResult.Ok(instance);
    }

    private async Task<ActivityResult> ExecuteActivityAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        ActivityDefinition activityDef,
        CancellationToken ct)
    {
        if (!_activities.TryGetValue(activityDef.Type, out var activity))
        {
            return ActivityResult.Fail("UNKNOWN_ACTIVITY_TYPE", $"Activity type '{activityDef.Type}' is not registered");
        }

        var execution = new ActivityExecution
        {
            WorkflowInstanceId = instance.Id,
            ActivityId = activityDef.Id,
            ActivityType = activityDef.Type,
            Status = ActivityStatus.Running,
            Attempt = instance.RetryCount + 1,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Resolve input
        var input = new Dictionary<string, object?>();
        foreach (var (inputKey, expression) in activityDef.InputMappings)
        {
            input[inputKey] = ResolveExpression(expression, instance);
        }
        execution.Input = input;

        var context = new ActivityContext
        {
            Instance = instance,
            Definition = activityDef,
            Input = input,
            CancellationToken = ct,
            Attempt = execution.Attempt,
            ServiceProvider = _serviceProvider
        };

        try
        {
            // Apply timeout
            var timeout = activityDef.Timeout ?? definition.Timeout ?? _options.DefaultTimeout;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            context = context with { CancellationToken = cts.Token };

            var result = await activity.ExecuteAsync(context);

            execution.Status = result.Success ? ActivityStatus.Completed : ActivityStatus.Failed;
            execution.Output = result.Output;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.DurationMs = (long)(execution.CompletedAt.Value - execution.StartedAt).TotalMilliseconds;

            if (!result.Success && result.Error is not null)
            {
                execution.Error = new WorkflowError
                {
                    Code = result.Error.Code,
                    Message = result.Error.Message,
                    ActivityId = activityDef.Id,
                    StackTrace = result.Error.Exception?.StackTrace
                };
            }

            await _executionRepository.CreateAsync(execution, ct);

            if (_options.EnableDetailedLogging)
            {
                _logger.LogDebug(
                    "Activity {ActivityId} ({ActivityType}) completed with status {Status} in {Duration}ms",
                    activityDef.Id, activityDef.Type, execution.Status, execution.DurationMs);
            }

            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            execution.Status = ActivityStatus.Failed;
            execution.Error = new WorkflowError
            {
                Code = "TIMEOUT",
                Message = "Activity timed out",
                ActivityId = activityDef.Id
            };
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.DurationMs = (long)(execution.CompletedAt.Value - execution.StartedAt).TotalMilliseconds;
            
            await _executionRepository.CreateAsync(execution, ct);
            
            return ActivityResult.Fail("TIMEOUT", "Activity timed out");
        }
        catch (Exception ex)
        {
            execution.Status = ActivityStatus.Failed;
            execution.Error = new WorkflowError
            {
                Code = ex.GetType().Name,
                Message = ex.Message,
                ActivityId = activityDef.Id,
                StackTrace = ex.StackTrace
            };
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.DurationMs = (long)(execution.CompletedAt.Value - execution.StartedAt).TotalMilliseconds;
            
            await _executionRepository.CreateAsync(execution, ct);
            
            return ActivityResult.Fail(ex);
        }
    }

    private async Task<WorkflowEngineResult> FailWorkflowAsync(
        WorkflowInstance instance,
        string code,
        string message,
        CancellationToken ct,
        string? activityId = null)
    {
        instance.Status = WorkflowStatus.Failed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.Error = new WorkflowError
        {
            Code = code,
            Message = message,
            ActivityId = activityId ?? instance.CurrentActivityId
        };
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        await _instanceRepository.UpdateAsync(instance, ct);

        _logger.LogError(
            "Workflow {InstanceId} failed at activity {ActivityId}: [{Code}] {Message}",
            instance.Id, instance.CurrentActivityId, code, message);

        return WorkflowEngineResult.Ok(instance);
    }

    private string? GetNextActivityId(WorkflowDefinition definition, string currentActivityId, WorkflowInstance instance)
    {
        var transitions = definition.Transitions
            .Where(t => t.From == currentActivityId)
            .OrderBy(t => t.Priority)
            .ToList();

        foreach (var transition in transitions)
        {
            if (string.IsNullOrEmpty(transition.Condition))
            {
                if (transition.IsDefault)
                    continue;
                return transition.To;
            }

            if (EvaluateCondition(transition.Condition, instance))
            {
                return transition.To;
            }
        }

        // Check for default transition
        return transitions.FirstOrDefault(t => t.IsDefault)?.To;
    }

    private bool EvaluateCondition(string condition, WorkflowInstance instance)
    {
        // Simple condition evaluation
        // Format: "state.key == value" or "state.key != value"
        var parts = condition.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return true;

        var leftValue = ResolveExpression(parts[0], instance);
        var op = parts[1];
        var rightValue = ResolveExpression(parts[2], instance);

        return op switch
        {
            "==" => Equals(leftValue?.ToString(), rightValue?.ToString()),
            "!=" => !Equals(leftValue?.ToString(), rightValue?.ToString()),
            ">" => CompareNumeric(leftValue, rightValue) > 0,
            "<" => CompareNumeric(leftValue, rightValue) < 0,
            ">=" => CompareNumeric(leftValue, rightValue) >= 0,
            "<=" => CompareNumeric(leftValue, rightValue) <= 0,
            _ => true
        };
    }

    private object? ResolveExpression(string expression, WorkflowInstance instance)
    {
        if (expression.StartsWith("state."))
            return instance.State.GetValueOrDefault(expression[6..]);
        if (expression.StartsWith("input."))
            return instance.Input.GetValueOrDefault(expression[6..]);
        if (expression.StartsWith('"') && expression.EndsWith('"'))
            return expression[1..^1];
        if (double.TryParse(expression, out var num))
            return num;
        if (bool.TryParse(expression, out var b))
            return b;
        return expression;
    }

    private static int CompareNumeric(object? left, object? right)
    {
        if (double.TryParse(left?.ToString(), out var leftNum) &&
            double.TryParse(right?.ToString(), out var rightNum))
        {
            return leftNum.CompareTo(rightNum);
        }
        return 0;
    }

    private static TimeSpan CalculateRetryDelay(int attempt, RetryPolicy policy)
    {
        var delay = policy.InitialDelay * Math.Pow(policy.BackoffMultiplier, attempt - 1);
        return TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, policy.MaxDelay.TotalMilliseconds));
    }

    private static string? ValidateInput(Dictionary<string, object?>? input, InputSchema schema)
    {
        input ??= new();
        
        foreach (var required in schema.Required)
        {
            if (!input.ContainsKey(required) || input[required] is null)
            {
                return $"Required input '{required}' is missing";
            }
        }

        foreach (var (name, prop) in schema.Properties)
        {
            if (!input.TryGetValue(name, out var value) || value is null)
                continue;

            var actualType = value.GetType().Name.ToLower();
            var expectedType = prop.Type.ToLower();

            // Basic type checking
            var isValid = expectedType switch
            {
                "string" => value is string,
                "number" => value is int or long or float or double or decimal,
                "integer" => value is int or long,
                "boolean" => value is bool,
                "array" => value is System.Collections.IEnumerable and not string,
                "object" => value is Dictionary<string, object?> or System.Text.Json.JsonElement,
                _ => true
            };

            if (!isValid)
            {
                return $"Input '{name}' expected type '{expectedType}' but got '{actualType}'";
            }
        }

        return null;
    }

    private static Dictionary<string, object?> BuildOutput(WorkflowDefinition definition, WorkflowInstance instance)
    {
        if (definition.OutputSchema is null)
            return instance.State;

        var output = new Dictionary<string, object?>();
        foreach (var (name, _) in definition.OutputSchema.Properties)
        {
            if (instance.State.TryGetValue(name, out var value))
            {
                output[name] = value;
            }
        }
        return output;
    }

    private void RegisterBuiltInActivities()
    {
        using var scope = _serviceProvider.CreateScope();
        
        RegisterActivity(new LogActivity(scope.ServiceProvider.GetRequiredService<ILogger<LogActivity>>()));
        RegisterActivity(new DelayActivity());
        RegisterActivity(scope.ServiceProvider.GetRequiredService<HttpActivity>());
        RegisterActivity(new TransformActivity());
        RegisterActivity(new ConditionActivity());
        RegisterActivity(new ForEachActivity());
        RegisterActivity(new SetStateActivity());
        RegisterActivity(new WaitForSignalActivity());
        RegisterActivity(new InvokeWorkflowActivity());
        RegisterActivity(new ParallelActivity());
    }
}
