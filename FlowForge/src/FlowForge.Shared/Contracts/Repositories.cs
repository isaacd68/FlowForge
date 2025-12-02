using FlowForge.Shared.Constants;
using FlowForge.Shared.DTOs;
using FlowForge.Shared.Models;

namespace FlowForge.Shared.Contracts;

/// <summary>
/// Repository for workflow definitions.
/// </summary>
public interface IWorkflowDefinitionRepository
{
    /// <summary>Get a workflow definition by name and version.</summary>
    Task<WorkflowDefinition?> GetAsync(string name, int? version = null, CancellationToken ct = default);
    
    /// <summary>Get all versions of a workflow definition.</summary>
    Task<IReadOnlyList<WorkflowDefinition>> GetAllVersionsAsync(string name, CancellationToken ct = default);
    
    /// <summary>List all workflow definitions (latest versions).</summary>
    Task<IReadOnlyList<WorkflowDefinition>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    
    /// <summary>Save a workflow definition (creates new version if exists).</summary>
    Task<WorkflowDefinition> SaveAsync(WorkflowDefinition definition, CancellationToken ct = default);
    
    /// <summary>Activate or deactivate a workflow definition.</summary>
    Task<bool> SetActiveAsync(string name, int version, bool isActive, CancellationToken ct = default);
    
    /// <summary>Delete a workflow definition version.</summary>
    Task<bool> DeleteAsync(string name, int version, CancellationToken ct = default);
    
    /// <summary>Check if a workflow definition exists.</summary>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);
}

/// <summary>
/// Repository for workflow instances.
/// </summary>
public interface IWorkflowInstanceRepository
{
    /// <summary>Get a workflow instance by ID.</summary>
    Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>Get workflow instances by correlation ID.</summary>
    Task<IReadOnlyList<WorkflowInstance>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default);
    
    /// <summary>Query workflow instances with filtering and pagination.</summary>
    Task<PagedResult<WorkflowInstance>> QueryAsync(WorkflowInstanceQuery query, CancellationToken ct = default);
    
    /// <summary>Get instances by status.</summary>
    Task<IReadOnlyList<WorkflowInstance>> GetByStatusAsync(WorkflowStatus status, int limit = 100, CancellationToken ct = default);
    
    /// <summary>Create a new workflow instance.</summary>
    Task<WorkflowInstance> CreateAsync(WorkflowInstance instance, CancellationToken ct = default);
    
    /// <summary>Update an existing workflow instance.</summary>
    Task<WorkflowInstance> UpdateAsync(WorkflowInstance instance, CancellationToken ct = default);
    
    /// <summary>Delete a workflow instance.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>Get timed out instances for cleanup.</summary>
    Task<IReadOnlyList<WorkflowInstance>> GetTimedOutInstancesAsync(TimeSpan timeout, CancellationToken ct = default);
    
    /// <summary>Get statistics for dashboard.</summary>
    Task<DashboardStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// Repository for activity executions.
/// </summary>
public interface IActivityExecutionRepository
{
    /// <summary>Get all executions for a workflow instance.</summary>
    Task<IReadOnlyList<ActivityExecution>> GetByInstanceAsync(Guid instanceId, CancellationToken ct = default);
    
    /// <summary>Get a specific execution.</summary>
    Task<ActivityExecution?> GetAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>Record an activity execution.</summary>
    Task<ActivityExecution> CreateAsync(ActivityExecution execution, CancellationToken ct = default);
    
    /// <summary>Update an activity execution.</summary>
    Task<ActivityExecution> UpdateAsync(ActivityExecution execution, CancellationToken ct = default);
    
    /// <summary>Get the latest execution for an activity.</summary>
    Task<ActivityExecution?> GetLatestAsync(Guid instanceId, string activityId, CancellationToken ct = default);
}

/// <summary>
/// Service for distributed locking.
/// </summary>
public interface IDistributedLockService
{
    /// <summary>Acquire a lock for a workflow instance.</summary>
    Task<IAsyncDisposable?> AcquireLockAsync(Guid instanceId, TimeSpan timeout, CancellationToken ct = default);
    
    /// <summary>Acquire a named lock.</summary>
    Task<IAsyncDisposable?> AcquireNamedLockAsync(string lockName, TimeSpan timeout, CancellationToken ct = default);
    
    /// <summary>Check if a lock exists.</summary>
    Task<bool> IsLockedAsync(Guid instanceId, CancellationToken ct = default);
}

/// <summary>
/// Service for caching.
/// </summary>
public interface ICacheService
{
    /// <summary>Get a cached value.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    
    /// <summary>Set a cached value.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    
    /// <summary>Remove a cached value.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
    
    /// <summary>Check if key exists.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    
    /// <summary>Get or create a cached value.</summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default);
}

/// <summary>
/// Message queue service for distributing work.
/// </summary>
public interface IMessageQueueService
{
    /// <summary>Publish a workflow job to the queue.</summary>
    Task PublishAsync(WorkflowJob job, CancellationToken ct = default);
    
    /// <summary>Subscribe to workflow jobs.</summary>
    Task SubscribeAsync(Func<WorkflowJob, CancellationToken, Task> handler, CancellationToken ct = default);
    
    /// <summary>Acknowledge job completion.</summary>
    Task AcknowledgeAsync(string messageId, CancellationToken ct = default);
    
    /// <summary>Reject a job (for retry or dead-letter).</summary>
    Task RejectAsync(string messageId, bool requeue = false, CancellationToken ct = default);
}

/// <summary>
/// Represents a job to be processed by a worker.
/// </summary>
public class WorkflowJob
{
    /// <summary>Message ID from queue.</summary>
    public string? MessageId { get; set; }
    
    /// <summary>Workflow instance ID.</summary>
    public Guid InstanceId { get; set; }
    
    /// <summary>Activity to execute.</summary>
    public string? ActivityId { get; set; }
    
    /// <summary>Job type.</summary>
    public WorkflowJobType Type { get; set; }
    
    /// <summary>When the job was queued.</summary>
    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>Priority (lower = higher priority).</summary>
    public int Priority { get; set; } = 100;
    
    /// <summary>Attempt number.</summary>
    public int Attempt { get; set; } = 1;
}

/// <summary>
/// Types of workflow jobs.
/// </summary>
public enum WorkflowJobType
{
    /// <summary>Start a new workflow.</summary>
    Start = 0,
    
    /// <summary>Continue executing a workflow.</summary>
    Continue = 1,
    
    /// <summary>Resume a suspended workflow.</summary>
    Resume = 2,
    
    /// <summary>Retry a failed activity.</summary>
    Retry = 3,
    
    /// <summary>Cancel a workflow.</summary>
    Cancel = 4
}
