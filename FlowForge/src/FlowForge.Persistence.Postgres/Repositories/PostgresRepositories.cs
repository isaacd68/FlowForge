using System.Text.Json;
using FlowForge.Shared.Constants;
using FlowForge.Shared.Contracts;
using FlowForge.Shared.DTOs;
using FlowForge.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Persistence.Postgres.Repositories;

/// <summary>
/// PostgreSQL implementation of workflow definition repository.
/// </summary>
public class PostgresWorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly FlowForgeDbContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PostgresWorkflowDefinitionRepository(FlowForgeDbContext context) => _context = context;

    public async Task<WorkflowDefinition?> GetAsync(string name, int? version = null, CancellationToken ct = default)
    {
        var query = _context.WorkflowDefinitions.Where(d => d.Name == name);
        
        if (version.HasValue)
        {
            query = query.Where(d => d.Version == version.Value);
        }
        else
        {
            query = query.Where(d => d.IsActive).OrderByDescending(d => d.Version);
        }

        var entity = await query.FirstOrDefaultAsync(ct);
        return entity is null ? null : MapToModel(entity);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllVersionsAsync(string name, CancellationToken ct = default)
    {
        var entities = await _context.WorkflowDefinitions
            .Where(d => d.Name == name)
            .OrderByDescending(d => d.Version)
            .ToListAsync(ct);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _context.WorkflowDefinitions.AsQueryable();
        
        if (!includeInactive)
        {
            query = query.Where(d => d.IsActive);
        }

        // Get latest version of each workflow
        var entities = await query
            .GroupBy(d => d.Name)
            .Select(g => g.OrderByDescending(d => d.Version).First())
            .ToListAsync(ct);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<WorkflowDefinition> SaveAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        var existing = await _context.WorkflowDefinitions
            .Where(d => d.Name == definition.Name)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);

        var entity = MapToEntity(definition);
        
        if (existing is not null)
        {
            entity.Version = existing.Version + 1;
            // Deactivate previous version
            existing.IsActive = false;
        }
        else
        {
            entity.Version = 1;
        }

        entity.CreatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        _context.WorkflowDefinitions.Add(entity);
        await _context.SaveChangesAsync(ct);

        definition.Version = entity.Version;
        definition.CreatedAt = entity.CreatedAt;
        definition.UpdatedAt = entity.UpdatedAt;

        return definition;
    }

    public async Task<bool> SetActiveAsync(string name, int version, bool isActive, CancellationToken ct = default)
    {
        var entity = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.Name == name && d.Version == version, ct);

        if (entity is null) return false;

        entity.IsActive = isActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string name, int version, CancellationToken ct = default)
    {
        var entity = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.Name == name && d.Version == version, ct);

        if (entity is null) return false;

        _context.WorkflowDefinitions.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        return await _context.WorkflowDefinitions.AnyAsync(d => d.Name == name, ct);
    }

    private static WorkflowDefinition MapToModel(WorkflowDefinitionEntity entity) => new()
    {
        Name = entity.Name,
        Version = entity.Version,
        Description = entity.Description,
        StartActivityId = entity.StartActivityId,
        Activities = string.IsNullOrEmpty(entity.ActivitiesJson) 
            ? new() 
            : JsonSerializer.Deserialize<List<ActivityDefinition>>(entity.ActivitiesJson, JsonOptions) ?? new(),
        Transitions = string.IsNullOrEmpty(entity.TransitionsJson)
            ? new()
            : JsonSerializer.Deserialize<List<TransitionDefinition>>(entity.TransitionsJson, JsonOptions) ?? new(),
        InputSchema = string.IsNullOrEmpty(entity.InputSchemaJson)
            ? null
            : JsonSerializer.Deserialize<InputSchema>(entity.InputSchemaJson, JsonOptions),
        OutputSchema = string.IsNullOrEmpty(entity.OutputSchemaJson)
            ? null
            : JsonSerializer.Deserialize<OutputSchema>(entity.OutputSchemaJson, JsonOptions),
        Trigger = string.IsNullOrEmpty(entity.TriggerJson)
            ? null
            : JsonSerializer.Deserialize<TriggerDefinition>(entity.TriggerJson, JsonOptions),
        DefaultRetryPolicy = string.IsNullOrEmpty(entity.DefaultRetryPolicyJson)
            ? null
            : JsonSerializer.Deserialize<RetryPolicy>(entity.DefaultRetryPolicyJson, JsonOptions),
        Timeout = entity.Timeout,
        IsActive = entity.IsActive,
        Tags = entity.Tags,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };

    private static WorkflowDefinitionEntity MapToEntity(WorkflowDefinition model) => new()
    {
        Name = model.Name,
        Version = model.Version,
        Description = model.Description,
        StartActivityId = model.StartActivityId,
        ActivitiesJson = JsonSerializer.Serialize(model.Activities, JsonOptions),
        TransitionsJson = JsonSerializer.Serialize(model.Transitions, JsonOptions),
        InputSchemaJson = model.InputSchema is null ? null : JsonSerializer.Serialize(model.InputSchema, JsonOptions),
        OutputSchemaJson = model.OutputSchema is null ? null : JsonSerializer.Serialize(model.OutputSchema, JsonOptions),
        TriggerJson = model.Trigger is null ? null : JsonSerializer.Serialize(model.Trigger, JsonOptions),
        DefaultRetryPolicyJson = model.DefaultRetryPolicy is null ? null : JsonSerializer.Serialize(model.DefaultRetryPolicy, JsonOptions),
        Timeout = model.Timeout,
        IsActive = model.IsActive,
        Tags = model.Tags,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt
    };
}

/// <summary>
/// PostgreSQL implementation of workflow instance repository.
/// </summary>
public class PostgresWorkflowInstanceRepository : IWorkflowInstanceRepository
{
    private readonly FlowForgeDbContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PostgresWorkflowInstanceRepository(FlowForgeDbContext context) => _context = context;

    public async Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == id, ct);
        return entity is null ? null : MapToModel(entity);
    }

    public async Task<IReadOnlyList<WorkflowInstance>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
    {
        var entities = await _context.WorkflowInstances
            .Where(i => i.CorrelationId == correlationId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<PagedResult<WorkflowInstance>> QueryAsync(WorkflowInstanceQuery query, CancellationToken ct = default)
    {
        var dbQuery = _context.WorkflowInstances.AsQueryable();

        if (!string.IsNullOrEmpty(query.WorkflowName))
            dbQuery = dbQuery.Where(i => i.WorkflowName == query.WorkflowName);

        if (query.Status.HasValue)
            dbQuery = dbQuery.Where(i => i.Status == query.Status.Value);

        if (!string.IsNullOrEmpty(query.CorrelationId))
            dbQuery = dbQuery.Where(i => i.CorrelationId == query.CorrelationId);

        if (query.CreatedAfter.HasValue)
            dbQuery = dbQuery.Where(i => i.CreatedAt >= query.CreatedAfter.Value);

        if (query.CreatedBefore.HasValue)
            dbQuery = dbQuery.Where(i => i.CreatedAt <= query.CreatedBefore.Value);

        if (query.Tags?.Any() == true)
            dbQuery = dbQuery.Where(i => query.Tags.All(t => i.Tags.Contains(t)));

        var totalCount = await dbQuery.CountAsync(ct);

        dbQuery = query.SortBy.ToLower() switch
        {
            "createdat" => query.SortDescending 
                ? dbQuery.OrderByDescending(i => i.CreatedAt) 
                : dbQuery.OrderBy(i => i.CreatedAt),
            "updatedat" => query.SortDescending 
                ? dbQuery.OrderByDescending(i => i.UpdatedAt) 
                : dbQuery.OrderBy(i => i.UpdatedAt),
            "status" => query.SortDescending 
                ? dbQuery.OrderByDescending(i => i.Status) 
                : dbQuery.OrderBy(i => i.Status),
            _ => query.SortDescending 
                ? dbQuery.OrderByDescending(i => i.CreatedAt) 
                : dbQuery.OrderBy(i => i.CreatedAt)
        };

        var entities = await dbQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<WorkflowInstance>
        {
            Items = entities.Select(MapToModel).ToList(),
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<IReadOnlyList<WorkflowInstance>> GetByStatusAsync(WorkflowStatus status, int limit = 100, CancellationToken ct = default)
    {
        var entities = await _context.WorkflowInstances
            .Where(i => i.Status == status)
            .OrderBy(i => i.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<WorkflowInstance> CreateAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        var entity = MapToEntity(instance);
        entity.CreatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        _context.WorkflowInstances.Add(entity);
        await _context.SaveChangesAsync(ct);

        instance.Id = entity.Id;
        instance.CreatedAt = entity.CreatedAt;
        instance.UpdatedAt = entity.UpdatedAt;

        return instance;
    }

    public async Task<WorkflowInstance> UpdateAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        var entity = await _context.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instance.Id, ct)
            ?? throw new InvalidOperationException($"Instance {instance.Id} not found");

        entity.Status = instance.Status;
        entity.InputJson = JsonSerializer.Serialize(instance.Input, JsonOptions);
        entity.OutputJson = JsonSerializer.Serialize(instance.Output, JsonOptions);
        entity.StateJson = JsonSerializer.Serialize(instance.State, JsonOptions);
        entity.ErrorJson = instance.Error is null ? null : JsonSerializer.Serialize(instance.Error, JsonOptions);
        entity.CurrentActivityId = instance.CurrentActivityId;
        entity.StartedAt = instance.StartedAt;
        entity.CompletedAt = instance.CompletedAt;
        entity.RetryCount = instance.RetryCount;
        entity.WorkerId = instance.WorkerId;
        entity.Tags = instance.Tags;
        entity.MetadataJson = JsonSerializer.Serialize(instance.Metadata, JsonOptions);
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);

        instance.UpdatedAt = entity.UpdatedAt;
        return instance;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (entity is null) return false;

        _context.WorkflowInstances.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<WorkflowInstance>> GetTimedOutInstancesAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - timeout;
        
        var entities = await _context.WorkflowInstances
            .Where(i => i.Status == WorkflowStatus.Running && i.UpdatedAt < cutoff)
            .ToListAsync(ct);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<DashboardStats> GetStatsAsync(CancellationToken ct = default)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var totalWorkflows = await _context.WorkflowDefinitions
            .Select(d => d.Name)
            .Distinct()
            .CountAsync(ct);

        var activeInstances = await _context.WorkflowInstances
            .CountAsync(i => i.Status == WorkflowStatus.Running || i.Status == WorkflowStatus.Suspended, ct);

        var completedToday = await _context.WorkflowInstances
            .CountAsync(i => i.Status == WorkflowStatus.Completed && 
                           i.CompletedAt >= today && i.CompletedAt < tomorrow, ct);

        var failedToday = await _context.WorkflowInstances
            .CountAsync(i => i.Status == WorkflowStatus.Failed && 
                           i.CompletedAt >= today && i.CompletedAt < tomorrow, ct);

        var totalToday = completedToday + failedToday;
        var successRate = totalToday > 0 ? (double)completedToday / totalToday * 100 : 100;

        var avgDuration = await _context.WorkflowInstances
            .Where(i => i.Status == WorkflowStatus.Completed && i.StartedAt.HasValue && i.CompletedAt.HasValue)
            .Select(i => (i.CompletedAt!.Value - i.StartedAt!.Value).TotalMilliseconds)
            .DefaultIfEmpty(0)
            .AverageAsync(ct);

        return new DashboardStats
        {
            TotalWorkflows = totalWorkflows,
            ActiveInstances = activeInstances,
            CompletedToday = completedToday,
            FailedToday = failedToday,
            SuccessRate = successRate,
            AverageDurationMs = avgDuration
        };
    }

    private static WorkflowInstance MapToModel(WorkflowInstanceEntity entity) => new()
    {
        Id = entity.Id,
        WorkflowName = entity.WorkflowName,
        WorkflowVersion = entity.WorkflowVersion,
        Status = entity.Status,
        Input = string.IsNullOrEmpty(entity.InputJson)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.InputJson, JsonOptions) ?? new(),
        Output = string.IsNullOrEmpty(entity.OutputJson)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.OutputJson, JsonOptions) ?? new(),
        State = string.IsNullOrEmpty(entity.StateJson)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.StateJson, JsonOptions) ?? new(),
        Error = string.IsNullOrEmpty(entity.ErrorJson)
            ? null
            : JsonSerializer.Deserialize<WorkflowError>(entity.ErrorJson, JsonOptions),
        ParentInstanceId = entity.ParentInstanceId,
        CorrelationId = entity.CorrelationId,
        CurrentActivityId = entity.CurrentActivityId,
        CreatedAt = entity.CreatedAt,
        StartedAt = entity.StartedAt,
        CompletedAt = entity.CompletedAt,
        UpdatedAt = entity.UpdatedAt,
        RetryCount = entity.RetryCount,
        WorkerId = entity.WorkerId,
        Tags = entity.Tags,
        Metadata = string.IsNullOrEmpty(entity.MetadataJson)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.MetadataJson, JsonOptions) ?? new()
    };

    private static WorkflowInstanceEntity MapToEntity(WorkflowInstance model) => new()
    {
        Id = model.Id,
        WorkflowName = model.WorkflowName,
        WorkflowVersion = model.WorkflowVersion,
        Status = model.Status,
        InputJson = JsonSerializer.Serialize(model.Input, JsonOptions),
        OutputJson = JsonSerializer.Serialize(model.Output, JsonOptions),
        StateJson = JsonSerializer.Serialize(model.State, JsonOptions),
        ErrorJson = model.Error is null ? null : JsonSerializer.Serialize(model.Error, JsonOptions),
        ParentInstanceId = model.ParentInstanceId,
        CorrelationId = model.CorrelationId,
        CurrentActivityId = model.CurrentActivityId,
        CreatedAt = model.CreatedAt,
        StartedAt = model.StartedAt,
        CompletedAt = model.CompletedAt,
        UpdatedAt = model.UpdatedAt,
        RetryCount = model.RetryCount,
        WorkerId = model.WorkerId,
        Tags = model.Tags,
        MetadataJson = JsonSerializer.Serialize(model.Metadata, JsonOptions)
    };
}

/// <summary>
/// PostgreSQL implementation of activity execution repository.
/// </summary>
public class PostgresActivityExecutionRepository : IActivityExecutionRepository
{
    private readonly FlowForgeDbContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PostgresActivityExecutionRepository(FlowForgeDbContext context) => _context = context;

    public async Task<IReadOnlyList<ActivityExecution>> GetByInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        var entities = await _context.ActivityExecutions
            .Where(e => e.WorkflowInstanceId == instanceId)
            .OrderBy(e => e.StartedAt)
            .ToListAsync(ct);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<ActivityExecution?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.ActivityExecutions.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is null ? null : MapToModel(entity);
    }

    public async Task<ActivityExecution> CreateAsync(ActivityExecution execution, CancellationToken ct = default)
    {
        var entity = MapToEntity(execution);
        _context.ActivityExecutions.Add(entity);
        await _context.SaveChangesAsync(ct);

        execution.Id = entity.Id;
        return execution;
    }

    public async Task<ActivityExecution> UpdateAsync(ActivityExecution execution, CancellationToken ct = default)
    {
        var entity = await _context.ActivityExecutions.FirstOrDefaultAsync(e => e.Id == execution.Id, ct)
            ?? throw new InvalidOperationException($"Execution {execution.Id} not found");

        entity.Status = execution.Status;
        entity.OutputJson = JsonSerializer.Serialize(execution.Output, JsonOptions);
        entity.ErrorJson = execution.Error is null ? null : JsonSerializer.Serialize(execution.Error, JsonOptions);
        entity.CompletedAt = execution.CompletedAt;
        entity.DurationMs = execution.DurationMs;

        await _context.SaveChangesAsync(ct);
        return execution;
    }

    public async Task<ActivityExecution?> GetLatestAsync(Guid instanceId, string activityId, CancellationToken ct = default)
    {
        var entity = await _context.ActivityExecutions
            .Where(e => e.WorkflowInstanceId == instanceId && e.ActivityId == activityId)
            .OrderByDescending(e => e.Attempt)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : MapToModel(entity);
    }

    private static ActivityExecution MapToModel(ActivityExecutionEntity entity) => new()
    {
        Id = entity.Id,
        WorkflowInstanceId = entity.WorkflowInstanceId,
        ActivityId = entity.ActivityId,
        ActivityType = entity.ActivityType,
        Status = entity.Status,
        Input = string.IsNullOrEmpty(entity.InputJson)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.InputJson, JsonOptions) ?? new(),
        Output = string.IsNullOrEmpty(entity.OutputJson)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.OutputJson, JsonOptions) ?? new(),
        Error = string.IsNullOrEmpty(entity.ErrorJson)
            ? null
            : JsonSerializer.Deserialize<WorkflowError>(entity.ErrorJson, JsonOptions),
        Attempt = entity.Attempt,
        StartedAt = entity.StartedAt,
        CompletedAt = entity.CompletedAt,
        DurationMs = entity.DurationMs,
        WorkerId = entity.WorkerId
    };

    private static ActivityExecutionEntity MapToEntity(ActivityExecution model) => new()
    {
        Id = model.Id,
        WorkflowInstanceId = model.WorkflowInstanceId,
        ActivityId = model.ActivityId,
        ActivityType = model.ActivityType,
        Status = model.Status,
        InputJson = JsonSerializer.Serialize(model.Input, JsonOptions),
        OutputJson = JsonSerializer.Serialize(model.Output, JsonOptions),
        ErrorJson = model.Error is null ? null : JsonSerializer.Serialize(model.Error, JsonOptions),
        Attempt = model.Attempt,
        StartedAt = model.StartedAt,
        CompletedAt = model.CompletedAt,
        DurationMs = model.DurationMs,
        WorkerId = model.WorkerId
    };
}
