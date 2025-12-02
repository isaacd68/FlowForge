using Cronos;
using FlowForge.Shared.Constants;
using FlowForge.Shared.Contracts;
using FlowForge.Shared.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Scheduling;

/// <summary>
/// Configuration for the workflow scheduler.
/// </summary>
public class SchedulerOptions
{
    /// <summary>Interval to check for due workflows.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(10);
    
    /// <summary>Maximum number of workflows to start per check.</summary>
    public int MaxStartsPerCheck { get; set; } = 100;
    
    /// <summary>Whether to run missed schedules on startup.</summary>
    public bool RunMissedOnStartup { get; set; } = false;
    
    /// <summary>Time zone for cron expressions.</summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;
}

/// <summary>
/// Tracks scheduled workflow executions.
/// </summary>
public class ScheduledWorkflow
{
    public required string WorkflowName { get; set; }
    public required int Version { get; set; }
    public required CronExpression CronExpression { get; set; }
    public DateTimeOffset? LastRun { get; set; }
    public DateTimeOffset? NextRun { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Background service that triggers scheduled workflows.
/// </summary>
public class WorkflowScheduler : BackgroundService
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IMessageQueueService _queueService;
    private readonly ILogger<WorkflowScheduler> _logger;
    private readonly SchedulerOptions _options;
    
    private readonly Dictionary<string, ScheduledWorkflow> _schedules = new();
    private readonly object _lock = new();

    public WorkflowScheduler(
        IWorkflowDefinitionRepository definitionRepository,
        IMessageQueueService queueService,
        ILogger<WorkflowScheduler> logger,
        SchedulerOptions options)
    {
        _definitionRepository = definitionRepository;
        _queueService = queueService;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workflow scheduler starting");
        
        // Load all scheduled workflows
        await RefreshSchedulesAsync(stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueWorkflowsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled workflows");
            }
            
            await Task.Delay(_options.CheckInterval, stoppingToken);
        }
        
        _logger.LogInformation("Workflow scheduler stopping");
    }

    /// <summary>
    /// Refresh the list of scheduled workflows from the database.
    /// </summary>
    public async Task RefreshSchedulesAsync(CancellationToken ct = default)
    {
        var definitions = await _definitionRepository.ListAsync(includeInactive: false, ct: ct);
        var now = DateTimeOffset.UtcNow;
        
        lock (_lock)
        {
            _schedules.Clear();
            
            foreach (var def in definitions)
            {
                if (def.Trigger?.Type != TriggerType.Scheduled || 
                    string.IsNullOrEmpty(def.Trigger.CronExpression))
                {
                    continue;
                }

                try
                {
                    var cronExpr = CronExpression.Parse(def.Trigger.CronExpression, CronFormat.IncludeSeconds);
                    var nextRun = cronExpr.GetNextOccurrence(now, _options.TimeZone);
                    
                    var schedule = new ScheduledWorkflow
                    {
                        WorkflowName = def.Name,
                        Version = def.Version,
                        CronExpression = cronExpr,
                        NextRun = nextRun,
                        IsEnabled = def.IsActive
                    };
                    
                    _schedules[def.Name] = schedule;
                    
                    _logger.LogDebug(
                        "Registered scheduled workflow {WorkflowName} with cron {Cron}, next run: {NextRun}",
                        def.Name, def.Trigger.CronExpression, nextRun);
                }
                catch (CronFormatException ex)
                {
                    _logger.LogWarning(
                        "Invalid cron expression for workflow {WorkflowName}: {Cron} - {Error}",
                        def.Name, def.Trigger.CronExpression, ex.Message);
                }
            }
        }
        
        _logger.LogInformation("Loaded {Count} scheduled workflows", _schedules.Count);
    }

    private async Task ProcessDueWorkflowsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var dueWorkflows = new List<ScheduledWorkflow>();
        
        lock (_lock)
        {
            foreach (var schedule in _schedules.Values)
            {
                if (!schedule.IsEnabled) continue;
                if (!schedule.NextRun.HasValue) continue;
                if (schedule.NextRun.Value > now) continue;
                
                dueWorkflows.Add(schedule);
                
                if (dueWorkflows.Count >= _options.MaxStartsPerCheck)
                    break;
            }
        }
        
        foreach (var schedule in dueWorkflows)
        {
            try
            {
                // Queue the workflow for execution
                var job = new WorkflowJob
                {
                    InstanceId = Guid.Empty, // Will be assigned when workflow is created
                    Type = WorkflowJobType.Start,
                    Priority = 50
                };
                
                // We need to create the instance first, but for now just log
                _logger.LogInformation(
                    "Triggering scheduled workflow {WorkflowName} (scheduled for {ScheduledTime})",
                    schedule.WorkflowName, schedule.NextRun);
                
                // Update next run time
                lock (_lock)
                {
                    schedule.LastRun = now;
                    schedule.NextRun = schedule.CronExpression.GetNextOccurrence(now, _options.TimeZone);
                }
                
                // In a full implementation, this would:
                // 1. Create a workflow instance via WorkflowEngine.StartWorkflowAsync
                // 2. Queue the job for a worker to pick up
                await _queueService.PublishAsync(job, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger scheduled workflow {WorkflowName}", schedule.WorkflowName);
            }
        }
    }

    /// <summary>
    /// Get information about all scheduled workflows.
    /// </summary>
    public IReadOnlyList<ScheduledWorkflowInfo> GetScheduledWorkflows()
    {
        lock (_lock)
        {
            return _schedules.Values.Select(s => new ScheduledWorkflowInfo
            {
                WorkflowName = s.WorkflowName,
                Version = s.Version,
                CronExpression = s.CronExpression.ToString(),
                LastRun = s.LastRun,
                NextRun = s.NextRun,
                IsEnabled = s.IsEnabled
            }).ToList();
        }
    }

    /// <summary>
    /// Enable or disable a scheduled workflow.
    /// </summary>
    public bool SetEnabled(string workflowName, bool enabled)
    {
        lock (_lock)
        {
            if (_schedules.TryGetValue(workflowName, out var schedule))
            {
                schedule.IsEnabled = enabled;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Manually trigger a scheduled workflow immediately.
    /// </summary>
    public async Task<bool> TriggerNowAsync(string workflowName, CancellationToken ct = default)
    {
        ScheduledWorkflow? schedule;
        lock (_lock)
        {
            _schedules.TryGetValue(workflowName, out schedule);
        }
        
        if (schedule is null)
            return false;
        
        var job = new WorkflowJob
        {
            InstanceId = Guid.Empty,
            Type = WorkflowJobType.Start,
            Priority = 10 // High priority for manual trigger
        };
        
        await _queueService.PublishAsync(job, ct);
        
        _logger.LogInformation("Manually triggered scheduled workflow {WorkflowName}", workflowName);
        return true;
    }
}

/// <summary>
/// Information about a scheduled workflow for API responses.
/// </summary>
public class ScheduledWorkflowInfo
{
    public required string WorkflowName { get; set; }
    public int Version { get; set; }
    public required string CronExpression { get; set; }
    public DateTimeOffset? LastRun { get; set; }
    public DateTimeOffset? NextRun { get; set; }
    public bool IsEnabled { get; set; }
}

/// <summary>
/// Utility methods for cron expressions.
/// </summary>
public static class CronHelper
{
    /// <summary>
    /// Validate a cron expression.
    /// </summary>
    public static bool IsValid(string expression, out string? error)
    {
        try
        {
            CronExpression.Parse(expression, CronFormat.IncludeSeconds);
            error = null;
            return true;
        }
        catch (CronFormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }
    
    /// <summary>
    /// Get the next N occurrences of a cron expression.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> GetNextOccurrences(
        string expression, 
        int count, 
        TimeZoneInfo? timeZone = null)
    {
        var cron = CronExpression.Parse(expression, CronFormat.IncludeSeconds);
        var tz = timeZone ?? TimeZoneInfo.Utc;
        var results = new List<DateTimeOffset>();
        var current = DateTimeOffset.UtcNow;
        
        for (int i = 0; i < count; i++)
        {
            var next = cron.GetNextOccurrence(current, tz);
            if (!next.HasValue) break;
            
            results.Add(next.Value);
            current = next.Value.AddSeconds(1);
        }
        
        return results;
    }
    
    /// <summary>
    /// Describe a cron expression in human-readable terms.
    /// </summary>
    public static string Describe(string expression)
    {
        // Basic descriptions for common patterns
        return expression switch
        {
            "0 * * * * *" => "Every minute",
            "0 */5 * * * *" => "Every 5 minutes",
            "0 */15 * * * *" => "Every 15 minutes",
            "0 */30 * * * *" => "Every 30 minutes",
            "0 0 * * * *" => "Every hour",
            "0 0 */2 * * *" => "Every 2 hours",
            "0 0 0 * * *" => "Daily at midnight",
            "0 0 9 * * *" => "Daily at 9:00 AM",
            "0 0 0 * * 0" => "Weekly on Sunday at midnight",
            "0 0 0 * * 1" => "Weekly on Monday at midnight",
            "0 0 0 1 * *" => "Monthly on the 1st at midnight",
            _ => $"Cron: {expression}"
        };
    }
}
