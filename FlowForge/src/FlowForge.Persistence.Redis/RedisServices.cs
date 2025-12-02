using System.Text.Json;
using FlowForge.Shared.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FlowForge.Persistence.Redis;

/// <summary>
/// Redis-based cache service implementation.
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly string _keyPrefix;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger,
        string keyPrefix = "flowforge:")
    {
        _redis = redis;
        _logger = logger;
        _keyPrefix = keyPrefix;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var fullKey = _keyPrefix + key;
        
        try
        {
            var value = await db.StringGetAsync(fullKey);
            if (value.IsNullOrEmpty)
                return default;

            return JsonSerializer.Deserialize<T>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cached value for key {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var fullKey = _keyPrefix + key;
        
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await db.StringSetAsync(fullKey, json, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set cached value for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var fullKey = _keyPrefix + key;
        
        try
        {
            await db.KeyDeleteAsync(fullKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove cached value for key {Key}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var fullKey = _keyPrefix + key;
        
        try
        {
            return await db.KeyExistsAsync(fullKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check existence for key {Key}", key);
            return false;
        }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null)
            return cached;

        var value = await factory();
        await SetAsync(key, value, expiry, ct);
        return value;
    }
}

/// <summary>
/// Redis-based distributed lock service.
/// </summary>
public class RedisDistributedLockService : IDistributedLockService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisDistributedLockService> _logger;
    private readonly string _keyPrefix;
    private readonly string _lockerId;

    public RedisDistributedLockService(
        IConnectionMultiplexer redis,
        ILogger<RedisDistributedLockService> logger,
        string keyPrefix = "flowforge:lock:")
    {
        _redis = redis;
        _logger = logger;
        _keyPrefix = keyPrefix;
        _lockerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    public async Task<IAsyncDisposable?> AcquireLockAsync(Guid instanceId, TimeSpan timeout, CancellationToken ct = default)
    {
        return await AcquireNamedLockAsync($"instance:{instanceId}", timeout, ct);
    }

    public async Task<IAsyncDisposable?> AcquireNamedLockAsync(string lockName, TimeSpan timeout, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var lockKey = _keyPrefix + lockName;
        var deadline = DateTime.UtcNow.Add(timeout);
        var retryDelay = TimeSpan.FromMilliseconds(50);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Try to acquire the lock using SET NX with expiry
                var acquired = await db.StringSetAsync(
                    lockKey,
                    _lockerId,
                    timeout,
                    When.NotExists);

                if (acquired)
                {
                    _logger.LogDebug("Acquired lock {LockName} with id {LockerId}", lockName, _lockerId);
                    return new RedisLockHandle(db, lockKey, _lockerId, _logger);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error acquiring lock {LockName}", lockName);
            }

            await Task.Delay(retryDelay, ct);
            retryDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds * 1.5, 500));
        }

        _logger.LogWarning("Failed to acquire lock {LockName} within timeout", lockName);
        return null;
    }

    public async Task<bool> IsLockedAsync(Guid instanceId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var lockKey = _keyPrefix + $"instance:{instanceId}";
        
        try
        {
            return await db.KeyExistsAsync(lockKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking lock status for instance {InstanceId}", instanceId);
            return false;
        }
    }

    private class RedisLockHandle : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly string _lockKey;
        private readonly string _lockerId;
        private readonly ILogger _logger;
        private bool _disposed;

        // Lua script to release lock only if we own it
        private const string ReleaseLockScript = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end";

        public RedisLockHandle(IDatabase db, string lockKey, string lockerId, ILogger logger)
        {
            _db = db;
            _lockKey = lockKey;
            _lockerId = lockerId;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                var result = await _db.ScriptEvaluateAsync(
                    ReleaseLockScript,
                    new RedisKey[] { _lockKey },
                    new RedisValue[] { _lockerId });

                if ((int)result == 1)
                {
                    _logger.LogDebug("Released lock {LockKey}", _lockKey);
                }
                else
                {
                    _logger.LogWarning("Lock {LockKey} was not owned by us or already expired", _lockKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing lock {LockKey}", _lockKey);
            }
        }
    }
}

/// <summary>
/// Redis-based message queue service for workflow jobs.
/// </summary>
public class RedisMessageQueueService : IMessageQueueService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisMessageQueueService> _logger;
    private readonly string _queueKey;
    private readonly string _processingKey;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisMessageQueueService(
        IConnectionMultiplexer redis,
        ILogger<RedisMessageQueueService> logger,
        string queueName = "flowforge:jobs")
    {
        _redis = redis;
        _logger = logger;
        _queueKey = queueName;
        _processingKey = queueName + ":processing";
    }

    public async Task PublishAsync(WorkflowJob job, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        job.MessageId = Guid.NewGuid().ToString("N");
        job.QueuedAt = DateTimeOffset.UtcNow;

        var json = JsonSerializer.Serialize(job, JsonOptions);
        
        // Use sorted set for priority queue (lower score = higher priority)
        var score = job.Priority + (job.QueuedAt.ToUnixTimeMilliseconds() / 1_000_000_000.0);
        await db.SortedSetAddAsync(_queueKey, json, score);
        
        _logger.LogDebug("Published job {MessageId} for instance {InstanceId}", job.MessageId, job.InstanceId);
    }

    public async Task SubscribeAsync(Func<WorkflowJob, CancellationToken, Task> handler, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Pop highest priority job (lowest score)
                var entries = await db.SortedSetRangeByRankAsync(_queueKey, 0, 0);
                
                if (entries.Length == 0)
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                var json = entries[0].ToString();
                
                // Try to remove it atomically
                var removed = await db.SortedSetRemoveAsync(_queueKey, json);
                if (!removed)
                    continue;

                var job = JsonSerializer.Deserialize<WorkflowJob>(json, JsonOptions);
                if (job is null)
                    continue;

                // Add to processing set
                await db.HashSetAsync(_processingKey, job.MessageId!, json);

                try
                {
                    await handler(job, ct);
                    await AcknowledgeAsync(job.MessageId!, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing job {MessageId}", job.MessageId);
                    await RejectAsync(job.MessageId!, requeue: job.Attempt < 3, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in message queue subscription");
                await Task.Delay(1000, ct);
            }
        }
    }

    public async Task AcknowledgeAsync(string messageId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.HashDeleteAsync(_processingKey, messageId);
        _logger.LogDebug("Acknowledged job {MessageId}", messageId);
    }

    public async Task RejectAsync(string messageId, bool requeue = false, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var json = await db.HashGetAsync(_processingKey, messageId);
        await db.HashDeleteAsync(_processingKey, messageId);

        if (requeue && !json.IsNullOrEmpty)
        {
            var job = JsonSerializer.Deserialize<WorkflowJob>(json!, JsonOptions);
            if (job is not null)
            {
                job.Attempt++;
                await PublishAsync(job, ct);
                _logger.LogDebug("Requeued job {MessageId} (attempt {Attempt})", messageId, job.Attempt);
            }
        }
        else
        {
            _logger.LogWarning("Rejected job {MessageId} (not requeued)", messageId);
        }
    }
}

/// <summary>
/// Extension methods for Redis service registration.
/// </summary>
public static class RedisServiceExtensions
{
    public static IServiceCollection AddFlowForgeRedis(
        this IServiceCollection services,
        string connectionString,
        Action<RedisOptions>? configure = null)
    {
        var options = new RedisOptions();
        configure?.Invoke(options);

        services.AddSingleton<IConnectionMultiplexer>(_ => 
            ConnectionMultiplexer.Connect(connectionString));

        services.AddSingleton<ICacheService>(sp => 
            new RedisCacheService(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<ILogger<RedisCacheService>>(),
                options.KeyPrefix));

        services.AddSingleton<IDistributedLockService>(sp => 
            new RedisDistributedLockService(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<ILogger<RedisDistributedLockService>>(),
                options.KeyPrefix + "lock:"));

        services.AddSingleton<IMessageQueueService>(sp => 
            new RedisMessageQueueService(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<ILogger<RedisMessageQueueService>>(),
                options.KeyPrefix + "jobs"));

        return services;
    }
}

/// <summary>
/// Redis configuration options.
/// </summary>
public class RedisOptions
{
    public string KeyPrefix { get; set; } = "flowforge:";
}

// Stub for IServiceCollection to avoid adding Microsoft.Extensions.DependencyInjection reference
#if !NETSTANDARD2_0_OR_GREATER
namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceCollection { }
}
#endif
