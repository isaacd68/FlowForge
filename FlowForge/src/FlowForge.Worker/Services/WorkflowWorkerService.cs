using FlowForge.Core.Workflows;
using FlowForge.Shared.Contracts;

namespace FlowForge.Worker.Services;

/// <summary>
/// Background worker that processes workflow jobs from the queue.
/// </summary>
public class WorkflowWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageQueueService _queueService;
    private readonly ILogger<WorkflowWorkerService> _logger;
    private readonly WorkerOptions _options;
    private readonly string _workerId;

    public WorkflowWorkerService(
        IServiceScopeFactory scopeFactory,
        IMessageQueueService queueService,
        ILogger<WorkflowWorkerService> logger,
        WorkerOptions options)
    {
        _scopeFactory = scopeFactory;
        _queueService = queueService;
        _logger = logger;
        _options = options;
        _workerId = $"{Environment.MachineName}:{Environment.ProcessId}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Worker {WorkerId} starting with concurrency {Concurrency}",
            _workerId, _options.MaxConcurrency);

        using var semaphore = new SemaphoreSlim(_options.MaxConcurrency);

        await _queueService.SubscribeAsync(async (job, ct) =>
        {
            await semaphore.WaitAsync(ct);
            
            try
            {
                await ProcessJobAsync(job, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }, stoppingToken);

        _logger.LogInformation("Worker {WorkerId} stopping", _workerId);
    }

    private async Task ProcessJobAsync(WorkflowJob job, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing job {MessageId} for instance {InstanceId} (type: {JobType})",
            job.MessageId, job.InstanceId, job.Type);

        using var scope = _scopeFactory.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();

        try
        {
            var result = job.Type switch
            {
                WorkflowJobType.Start or 
                WorkflowJobType.Continue or 
                WorkflowJobType.Resume => await engine.ExecuteAsync(job.InstanceId, ct),
                
                WorkflowJobType.Cancel => await engine.CancelAsync(job.InstanceId, ct),
                
                WorkflowJobType.Retry => await engine.ExecuteAsync(job.InstanceId, ct),
                
                _ => throw new InvalidOperationException($"Unknown job type: {job.Type}")
            };

            if (result.Success)
            {
                _logger.LogInformation(
                    "Job {MessageId} completed successfully, instance status: {Status}",
                    job.MessageId, result.Instance?.Status);
            }
            else
            {
                _logger.LogWarning(
                    "Job {MessageId} failed: [{ErrorCode}] {ErrorMessage}",
                    job.MessageId, result.ErrorCode, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {MessageId}", job.MessageId);
            throw;
        }
    }
}

/// <summary>
/// Worker configuration options.
/// </summary>
public class WorkerOptions
{
    /// <summary>Maximum concurrent jobs.</summary>
    public int MaxConcurrency { get; set; } = 10;
    
    /// <summary>Heartbeat interval.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>Supported activity types (empty = all).</summary>
    public List<string> SupportedActivities { get; set; } = new();
}

/// <summary>
/// Service that sends worker heartbeats for monitoring.
/// </summary>
public class WorkerHeartbeatService : BackgroundService
{
    private readonly ICacheService _cache;
    private readonly ILogger<WorkerHeartbeatService> _logger;
    private readonly WorkerOptions _options;
    private readonly string _workerId;

    public WorkerHeartbeatService(
        ICacheService cache,
        ILogger<WorkerHeartbeatService> logger,
        WorkerOptions options)
    {
        _cache = cache;
        _logger = logger;
        _options = options;
        _workerId = $"{Environment.MachineName}:{Environment.ProcessId}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var heartbeat = new WorkerHeartbeat
                {
                    WorkerId = _workerId,
                    Hostname = Environment.MachineName,
                    ProcessId = Environment.ProcessId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Status = "Online",
                    MaxConcurrency = _options.MaxConcurrency,
                    SupportedActivities = _options.SupportedActivities
                };

                await _cache.SetAsync(
                    $"worker:{_workerId}",
                    heartbeat,
                    _options.HeartbeatInterval * 3,
                    stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send heartbeat");
            }

            await Task.Delay(_options.HeartbeatInterval, stoppingToken);
        }
    }
}

/// <summary>
/// Worker heartbeat data.
/// </summary>
public class WorkerHeartbeat
{
    public required string WorkerId { get; set; }
    public required string Hostname { get; set; }
    public int ProcessId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string Status { get; set; }
    public int MaxConcurrency { get; set; }
    public List<string> SupportedActivities { get; set; } = new();
}
