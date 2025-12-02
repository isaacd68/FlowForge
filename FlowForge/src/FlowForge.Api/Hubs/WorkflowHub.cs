using Microsoft.AspNetCore.SignalR;

namespace FlowForge.Api.Hubs;

/// <summary>
/// SignalR hub for real-time workflow updates.
/// </summary>
public class WorkflowHub : Hub<IWorkflowHubClient>
{
    private readonly ILogger<WorkflowHub> _logger;

    public WorkflowHub(ILogger<WorkflowHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to updates for a specific workflow instance.
    /// </summary>
    public async Task SubscribeToWorkflow(Guid instanceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"workflow:{instanceId}");
        _logger.LogDebug("Client {ConnectionId} subscribed to workflow {InstanceId}", 
            Context.ConnectionId, instanceId);
    }

    /// <summary>
    /// Unsubscribe from workflow instance updates.
    /// </summary>
    public async Task UnsubscribeFromWorkflow(Guid instanceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"workflow:{instanceId}");
        _logger.LogDebug("Client {ConnectionId} unsubscribed from workflow {InstanceId}", 
            Context.ConnectionId, instanceId);
    }

    /// <summary>
    /// Subscribe to all updates for a workflow type.
    /// </summary>
    public async Task SubscribeToWorkflowType(string workflowName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"type:{workflowName}");
    }

    /// <summary>
    /// Unsubscribe from workflow type updates.
    /// </summary>
    public async Task UnsubscribeFromWorkflowType(string workflowName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"type:{workflowName}");
    }
}

/// <summary>
/// Client interface for strongly-typed SignalR hub.
/// </summary>
public interface IWorkflowHubClient
{
    /// <summary>Notified when a workflow starts.</summary>
    Task WorkflowStarted(WorkflowEventDto evt);
    
    /// <summary>Notified when a workflow status changes.</summary>
    Task WorkflowUpdated(WorkflowEventDto evt);
    
    /// <summary>Notified when an activity starts.</summary>
    Task ActivityStarted(ActivityEventDto evt);
    
    /// <summary>Notified when an activity completes.</summary>
    Task ActivityCompleted(ActivityEventDto evt);
    
    /// <summary>Notified when a workflow completes.</summary>
    Task WorkflowCompleted(WorkflowEventDto evt);
    
    /// <summary>Notified when a workflow fails.</summary>
    Task WorkflowFailed(WorkflowErrorEventDto evt);
}

/// <summary>
/// Event data for workflow updates.
/// </summary>
public class WorkflowEventDto
{
    public Guid InstanceId { get; set; }
    public required string WorkflowName { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// Event data for activity updates.
/// </summary>
public class ActivityEventDto
{
    public Guid InstanceId { get; set; }
    public required string ActivityId { get; set; }
    public required string ActivityType { get; set; }
    public required string Status { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// Event data for workflow errors.
/// </summary>
public class WorkflowErrorEventDto
{
    public Guid InstanceId { get; set; }
    public required string WorkflowName { get; set; }
    public required string ErrorCode { get; set; }
    public required string ErrorMessage { get; set; }
    public string? ActivityId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
