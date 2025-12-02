using System.Text.Json;
using FlowForge.Shared.Constants;
using FlowForge.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FlowForge.Persistence.Postgres;

/// <summary>
/// Entity Framework Core database context for FlowForge.
/// </summary>
public class FlowForgeDbContext : DbContext
{
    public FlowForgeDbContext(DbContextOptions<FlowForgeDbContext> options) : base(options) { }

    public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions => Set<WorkflowDefinitionEntity>();
    public DbSet<WorkflowInstanceEntity> WorkflowInstances => Set<WorkflowInstanceEntity>();
    public DbSet<ActivityExecutionEntity> ActivityExecutions => Set<ActivityExecutionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // JSON serialization options
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Workflow Definition
        modelBuilder.Entity<WorkflowDefinitionEntity>(entity =>
        {
            entity.ToTable("workflow_definitions");
            entity.HasKey(e => new { e.Name, e.Version });
            
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            
            entity.Property(e => e.ActivitiesJson)
                .HasColumnName("activities")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.TransitionsJson)
                .HasColumnName("transitions")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.InputSchemaJson)
                .HasColumnName("input_schema")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.OutputSchemaJson)
                .HasColumnName("output_schema")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.TriggerJson)
                .HasColumnName("trigger")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.DefaultRetryPolicyJson)
                .HasColumnName("default_retry_policy")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.Tags)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, jsonOptions),
                    v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new List<string>(),
                    new ValueComparer<List<string>>(
                        (c1, c2) => c1!.SequenceEqual(c2!),
                        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                        c => c.ToList()))
                .HasColumnType("jsonb");

            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.CreatedAt);
        });

        // Workflow Instance
        modelBuilder.Entity<WorkflowInstanceEntity>(entity =>
        {
            entity.ToTable("workflow_instances");
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.WorkflowName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(256);
            entity.Property(e => e.CurrentActivityId).HasMaxLength(256);
            entity.Property(e => e.WorkerId).HasMaxLength(256);
            
            entity.Property(e => e.InputJson)
                .HasColumnName("input")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.OutputJson)
                .HasColumnName("output")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.StateJson)
                .HasColumnName("state")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.ErrorJson)
                .HasColumnName("error")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.Tags)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, jsonOptions),
                    v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new List<string>(),
                    new ValueComparer<List<string>>(
                        (c1, c2) => c1!.SequenceEqual(c2!),
                        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                        c => c.ToList()))
                .HasColumnType("jsonb");
            
            entity.Property(e => e.MetadataJson)
                .HasColumnName("metadata")
                .HasColumnType("jsonb");

            entity.HasIndex(e => e.WorkflowName);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => e.ParentInstanceId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.UpdatedAt);
            entity.HasIndex(e => new { e.Status, e.UpdatedAt });
        });

        // Activity Execution
        modelBuilder.Entity<ActivityExecutionEntity>(entity =>
        {
            entity.ToTable("activity_executions");
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.ActivityId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ActivityType).HasMaxLength(256).IsRequired();
            entity.Property(e => e.WorkerId).HasMaxLength(256);
            
            entity.Property(e => e.InputJson)
                .HasColumnName("input")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.OutputJson)
                .HasColumnName("output")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.ErrorJson)
                .HasColumnName("error")
                .HasColumnType("jsonb");

            entity.HasIndex(e => e.WorkflowInstanceId);
            entity.HasIndex(e => new { e.WorkflowInstanceId, e.ActivityId });
            entity.HasIndex(e => e.StartedAt);
        });
    }
}

// Entity classes with JSON columns stored as strings

public class WorkflowDefinitionEntity
{
    public required string Name { get; set; }
    public int Version { get; set; }
    public string? Description { get; set; }
    public required string StartActivityId { get; set; }
    public string? ActivitiesJson { get; set; }
    public string? TransitionsJson { get; set; }
    public string? InputSchemaJson { get; set; }
    public string? OutputSchemaJson { get; set; }
    public string? TriggerJson { get; set; }
    public string? DefaultRetryPolicyJson { get; set; }
    public TimeSpan? Timeout { get; set; }
    public bool IsActive { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class WorkflowInstanceEntity
{
    public Guid Id { get; set; }
    public required string WorkflowName { get; set; }
    public int WorkflowVersion { get; set; }
    public WorkflowStatus Status { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? StateJson { get; set; }
    public string? ErrorJson { get; set; }
    public Guid? ParentInstanceId { get; set; }
    public string? CorrelationId { get; set; }
    public string? CurrentActivityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RetryCount { get; set; }
    public string? WorkerId { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? MetadataJson { get; set; }
}

public class ActivityExecutionEntity
{
    public Guid Id { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public required string ActivityId { get; set; }
    public required string ActivityType { get; set; }
    public ActivityStatus Status { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorJson { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? WorkerId { get; set; }
}
