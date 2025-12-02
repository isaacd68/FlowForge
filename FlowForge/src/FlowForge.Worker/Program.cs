using FlowForge.Core;
using FlowForge.Persistence.Postgres;
using FlowForge.Persistence.Postgres.Repositories;
using FlowForge.Persistence.Redis;
using FlowForge.Shared.Contracts;
using FlowForge.Worker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole();

// Add PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("Postgres") 
    ?? "Host=localhost;Database=flowforge;Username=postgres;Password=postgres";
builder.Services.AddDbContext<FlowForgeDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register repositories
builder.Services.AddScoped<IWorkflowDefinitionRepository, PostgresWorkflowDefinitionRepository>();
builder.Services.AddScoped<IWorkflowInstanceRepository, PostgresWorkflowInstanceRepository>();
builder.Services.AddScoped<IActivityExecutionRepository, PostgresActivityExecutionRepository>();

// Add Redis
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddFlowForgeRedis(redisConnection);

// Add FlowForge core
builder.Services.AddFlowForge(options =>
{
    options.EnableScheduler = false; // Scheduler runs in API
});

// Configure worker options
var workerOptions = new WorkerOptions
{
    MaxConcurrency = builder.Configuration.GetValue("Worker:MaxConcurrency", 10),
    HeartbeatInterval = TimeSpan.FromSeconds(
        builder.Configuration.GetValue("Worker:HeartbeatIntervalSeconds", 30))
};
builder.Services.AddSingleton(workerOptions);

// Add worker services
builder.Services.AddHostedService<WorkflowWorkerService>();
builder.Services.AddHostedService<WorkerHeartbeatService>();

var host = builder.Build();
host.Run();
