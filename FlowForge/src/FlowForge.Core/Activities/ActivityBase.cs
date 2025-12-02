using FlowForge.Shared.Models;

namespace FlowForge.Core.Activities;

/// <summary>
/// Context provided to activities during execution.
/// </summary>
public class ActivityContext
{
    /// <summary>The workflow instance being executed.</summary>
    public required WorkflowInstance Instance { get; init; }
    
    /// <summary>The activity definition.</summary>
    public required ActivityDefinition Definition { get; init; }
    
    /// <summary>Input data resolved for this activity.</summary>
    public Dictionary<string, object?> Input { get; init; } = new();
    
    /// <summary>Access to workflow state.</summary>
    public Dictionary<string, object?> State => Instance.State;
    
    /// <summary>Cancellation token for the operation.</summary>
    public CancellationToken CancellationToken { get; init; }
    
    /// <summary>Current attempt number (1-based).</summary>
    public int Attempt { get; init; } = 1;
    
    /// <summary>Service provider for resolving dependencies.</summary>
    public IServiceProvider? ServiceProvider { get; init; }
    
    /// <summary>Get a required service.</summary>
    public T GetRequiredService<T>() where T : notnull
    {
        if (ServiceProvider is null)
            throw new InvalidOperationException("ServiceProvider is not available");
        return (T)(ServiceProvider.GetService(typeof(T)) 
            ?? throw new InvalidOperationException($"Service {typeof(T).Name} not found"));
    }
    
    /// <summary>Get an optional service.</summary>
    public T? GetService<T>() where T : class
    {
        return ServiceProvider?.GetService(typeof(T)) as T;
    }
}

/// <summary>
/// Result of executing an activity.
/// </summary>
public class ActivityResult
{
    /// <summary>Whether the activity succeeded.</summary>
    public bool Success { get; init; }
    
    /// <summary>Output data from the activity.</summary>
    public Dictionary<string, object?> Output { get; init; } = new();
    
    /// <summary>Error if failed.</summary>
    public ActivityError? Error { get; init; }
    
    /// <summary>Whether to suspend the workflow and wait for external input.</summary>
    public bool Suspend { get; init; }
    
    /// <summary>Key to wait for when suspended.</summary>
    public string? SuspendKey { get; init; }
    
    /// <summary>Next activity to transition to (overrides normal flow).</summary>
    public string? NextActivityId { get; init; }
    
    /// <summary>Create a successful result.</summary>
    public static ActivityResult Ok(Dictionary<string, object?>? output = null) => new()
    {
        Success = true,
        Output = output ?? new()
    };
    
    /// <summary>Create a successful result with output.</summary>
    public static ActivityResult Ok(params (string Key, object? Value)[] output) => new()
    {
        Success = true,
        Output = output.ToDictionary(x => x.Key, x => x.Value)
    };
    
    /// <summary>Create a failed result.</summary>
    public static ActivityResult Fail(string code, string message, Exception? ex = null) => new()
    {
        Success = false,
        Error = new ActivityError
        {
            Code = code,
            Message = message,
            Exception = ex
        }
    };
    
    /// <summary>Create a failed result from exception.</summary>
    public static ActivityResult Fail(Exception ex) => new()
    {
        Success = false,
        Error = new ActivityError
        {
            Code = ex.GetType().Name,
            Message = ex.Message,
            Exception = ex
        }
    };
    
    /// <summary>Create a suspend result.</summary>
    public static ActivityResult SuspendFor(string key) => new()
    {
        Success = true,
        Suspend = true,
        SuspendKey = key
    };
}

/// <summary>
/// Error information from activity execution.
/// </summary>
public class ActivityError
{
    /// <summary>Error code.</summary>
    public required string Code { get; init; }
    
    /// <summary>Error message.</summary>
    public required string Message { get; init; }
    
    /// <summary>Original exception if available.</summary>
    public Exception? Exception { get; init; }
    
    /// <summary>Whether this error can be retried.</summary>
    public bool Retriable { get; init; } = true;
}

/// <summary>
/// Interface for activity implementations.
/// </summary>
public interface IActivity
{
    /// <summary>Unique type identifier for this activity.</summary>
    string Type { get; }
    
    /// <summary>Execute the activity.</summary>
    Task<ActivityResult> ExecuteAsync(ActivityContext context);
}

/// <summary>
/// Base class for activity implementations.
/// </summary>
public abstract class ActivityBase : IActivity
{
    /// <inheritdoc/>
    public abstract string Type { get; }
    
    /// <inheritdoc/>
    public abstract Task<ActivityResult> ExecuteAsync(ActivityContext context);
    
    /// <summary>Get a required property from the definition.</summary>
    protected T GetProperty<T>(ActivityContext context, string name)
    {
        if (!context.Definition.Properties.TryGetValue(name, out var value))
            throw new InvalidOperationException($"Required property '{name}' not found");
        
        return ConvertValue<T>(value, name);
    }
    
    /// <summary>Get an optional property from the definition.</summary>
    protected T? GetPropertyOrDefault<T>(ActivityContext context, string name, T? defaultValue = default)
    {
        if (!context.Definition.Properties.TryGetValue(name, out var value) || value is null)
            return defaultValue;
        
        return ConvertValue<T>(value, name);
    }
    
    /// <summary>Get a required input value.</summary>
    protected T GetInput<T>(ActivityContext context, string name)
    {
        if (!context.Input.TryGetValue(name, out var value))
            throw new InvalidOperationException($"Required input '{name}' not found");
        
        return ConvertValue<T>(value, name);
    }
    
    /// <summary>Get an optional input value.</summary>
    protected T? GetInputOrDefault<T>(ActivityContext context, string name, T? defaultValue = default)
    {
        if (!context.Input.TryGetValue(name, out var value) || value is null)
            return defaultValue;
        
        return ConvertValue<T>(value, name);
    }
    
    private static T ConvertValue<T>(object? value, string name)
    {
        if (value is null)
            throw new InvalidOperationException($"Value for '{name}' is null");
        
        if (value is T typedValue)
            return typedValue;
        
        try
        {
            if (value is System.Text.Json.JsonElement element)
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(element.GetRawText())!;
            }
            
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot convert value for '{name}' from {value.GetType().Name} to {typeof(T).Name}", ex);
        }
    }
}
