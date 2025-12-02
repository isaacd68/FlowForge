using FlowForge.Api.Hubs;
using FlowForge.Api.Middleware;
using FlowForge.Core;
using FlowForge.Persistence.Postgres;
using FlowForge.Persistence.Postgres.Repositories;
using FlowForge.Persistence.Redis;
using FlowForge.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "FlowForge API",
        Version = "v1",
        Description = "Distributed Workflow Engine API"
    });
});

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:3000" })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

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
    options.EnableScheduler = builder.Configuration.GetValue("FlowForge:EnableScheduler", true);
    options.EngineOptions.EnableDetailedLogging = builder.Configuration.GetValue("FlowForge:DetailedLogging", true);
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString)
    .AddRedis(redisConnection);

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "FlowForge API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseCors();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapHub<WorkflowHub>("/hubs/workflow");
app.MapHealthChecks("/health");

// Auto-migrate database in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<FlowForgeDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
