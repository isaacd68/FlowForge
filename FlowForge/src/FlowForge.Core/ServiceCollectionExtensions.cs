using FlowForge.Core.Activities;
using FlowForge.Core.Expressions;
using FlowForge.Core.Scheduling;
using FlowForge.Core.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace FlowForge.Core;

/// <summary>
/// Extension methods for configuring FlowForge services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add FlowForge core services to the service collection.
    /// </summary>
    public static IServiceCollection AddFlowForge(
        this IServiceCollection services,
        Action<FlowForgeOptions>? configure = null)
    {
        var options = new FlowForgeOptions();
        configure?.Invoke(options);
        
        // Register options
        services.AddSingleton(options.EngineOptions);
        services.AddSingleton(options.SchedulerOptions);
        
        // Register core services
        services.AddSingleton<WorkflowEngine>();
        services.AddSingleton<IExpressionEvaluator, JintExpressionEvaluator>();
        
        // Register HTTP client for HttpActivity
        services.AddHttpClient();
        services.AddTransient<HttpActivity>();
        
        // Register scheduler if enabled
        if (options.EnableScheduler)
        {
            services.AddHostedService<WorkflowScheduler>();
        }
        
        return services;
    }
    
    /// <summary>
    /// Add a custom activity to FlowForge.
    /// </summary>
    public static IServiceCollection AddFlowForgeActivity<TActivity>(this IServiceCollection services)
        where TActivity : class, IActivity
    {
        services.AddTransient<IActivity, TActivity>();
        services.AddTransient<TActivity>();
        return services;
    }
}

/// <summary>
/// Configuration options for FlowForge.
/// </summary>
public class FlowForgeOptions
{
    /// <summary>Workflow engine options.</summary>
    public WorkflowEngineOptions EngineOptions { get; set; } = new();
    
    /// <summary>Scheduler options.</summary>
    public SchedulerOptions SchedulerOptions { get; set; } = new();
    
    /// <summary>Whether to enable the background scheduler.</summary>
    public bool EnableScheduler { get; set; } = true;
}
