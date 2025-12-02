using FlowForge.Core.Scheduling;
using FlowForge.Shared.Contracts;
using FlowForge.Shared.DTOs;
using FlowForge.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlowForge.Api.Controllers;

/// <summary>
/// API endpoints for workflow definition management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DefinitionsController : ControllerBase
{
    private readonly IWorkflowDefinitionRepository _repository;
    private readonly ILogger<DefinitionsController> _logger;

    public DefinitionsController(
        IWorkflowDefinitionRepository repository,
        ILogger<DefinitionsController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// List all workflow definitions.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<WorkflowDefinitionSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<WorkflowDefinitionSummary>>> ListDefinitions(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var definitions = await _repository.ListAsync(includeInactive, ct);

        return definitions.Select(d => new WorkflowDefinitionSummary
        {
            Name = d.Name,
            Version = d.Version,
            Description = d.Description,
            ActivityCount = d.Activities.Count,
            IsActive = d.IsActive,
            Tags = d.Tags,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        }).ToList();
    }

    /// <summary>
    /// Get a workflow definition by name.
    /// </summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(WorkflowDefinition), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowDefinition>> GetDefinition(
        string name,
        [FromQuery] int? version = null,
        CancellationToken ct = default)
    {
        var definition = await _repository.GetAsync(name, version, ct);
        if (definition is null)
            return NotFound();

        return definition;
    }

    /// <summary>
    /// Get all versions of a workflow definition.
    /// </summary>
    [HttpGet("{name}/versions")]
    [ProducesResponseType(typeof(List<WorkflowDefinitionSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<WorkflowDefinitionSummary>>> GetVersions(
        string name,
        CancellationToken ct = default)
    {
        var versions = await _repository.GetAllVersionsAsync(name, ct);

        return versions.Select(d => new WorkflowDefinitionSummary
        {
            Name = d.Name,
            Version = d.Version,
            Description = d.Description,
            ActivityCount = d.Activities.Count,
            IsActive = d.IsActive,
            Tags = d.Tags,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        }).ToList();
    }

    /// <summary>
    /// Create or update a workflow definition.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowDefinitionSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowDefinitionSummary>> CreateDefinition(
        [FromBody] CreateWorkflowDefinitionRequest request,
        CancellationToken ct = default)
    {
        // Validate the request
        var validationErrors = ValidateDefinition(request);
        if (validationErrors.Any())
        {
            return ValidationProblem(new ValidationProblemDetails(validationErrors));
        }

        var definition = MapToModel(request);
        var saved = await _repository.SaveAsync(definition, ct);

        _logger.LogInformation(
            "Created workflow definition {Name} v{Version}",
            saved.Name, saved.Version);

        var summary = new WorkflowDefinitionSummary
        {
            Name = saved.Name,
            Version = saved.Version,
            Description = saved.Description,
            ActivityCount = saved.Activities.Count,
            IsActive = saved.IsActive,
            Tags = saved.Tags,
            CreatedAt = saved.CreatedAt,
            UpdatedAt = saved.UpdatedAt
        };

        return CreatedAtAction(
            nameof(GetDefinition),
            new { name = saved.Name },
            summary);
    }

    /// <summary>
    /// Activate a workflow definition version.
    /// </summary>
    [HttpPost("{name}/versions/{version:int}/activate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateVersion(
        string name,
        int version,
        CancellationToken ct = default)
    {
        var success = await _repository.SetActiveAsync(name, version, true, ct);
        if (!success)
            return NotFound();

        return Ok();
    }

    /// <summary>
    /// Deactivate a workflow definition version.
    /// </summary>
    [HttpPost("{name}/versions/{version:int}/deactivate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateVersion(
        string name,
        int version,
        CancellationToken ct = default)
    {
        var success = await _repository.SetActiveAsync(name, version, false, ct);
        if (!success)
            return NotFound();

        return Ok();
    }

    /// <summary>
    /// Delete a workflow definition version.
    /// </summary>
    [HttpDelete("{name}/versions/{version:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVersion(
        string name,
        int version,
        CancellationToken ct = default)
    {
        var success = await _repository.DeleteAsync(name, version, ct);
        if (!success)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Validate a cron expression.
    /// </summary>
    [HttpPost("validate-cron")]
    [ProducesResponseType(typeof(CronValidationResult), StatusCodes.Status200OK)]
    public ActionResult<CronValidationResult> ValidateCron([FromBody] string expression)
    {
        var isValid = CronHelper.IsValid(expression, out var error);
        
        return new CronValidationResult
        {
            IsValid = isValid,
            Error = error,
            Description = isValid ? CronHelper.Describe(expression) : null,
            NextOccurrences = isValid ? CronHelper.GetNextOccurrences(expression, 5) : null
        };
    }

    private static Dictionary<string, string[]> ValidateDefinition(CreateWorkflowDefinitionRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
            errors["Name"] = new[] { "Workflow name is required" };

        if (!request.Activities.Any())
            errors["Activities"] = new[] { "At least one activity is required" };

        if (string.IsNullOrWhiteSpace(request.StartActivityId))
            errors["StartActivityId"] = new[] { "Start activity ID is required" };

        var activityIds = request.Activities.Select(a => a.Id).ToHashSet();

        if (!string.IsNullOrEmpty(request.StartActivityId) && !activityIds.Contains(request.StartActivityId))
            errors["StartActivityId"] = new[] { $"Start activity '{request.StartActivityId}' not found in activities" };

        // Validate transitions reference valid activities
        var invalidTransitions = request.Transitions
            .Where(t => !activityIds.Contains(t.From) || !activityIds.Contains(t.To))
            .ToList();

        if (invalidTransitions.Any())
            errors["Transitions"] = new[] { "Some transitions reference non-existent activities" };

        // Validate cron expression if present
        if (request.Trigger?.Type == Shared.Constants.TriggerType.Scheduled &&
            !string.IsNullOrEmpty(request.Trigger.CronExpression))
        {
            if (!CronHelper.IsValid(request.Trigger.CronExpression, out var cronError))
                errors["Trigger.CronExpression"] = new[] { cronError! };
        }

        return errors;
    }

    private static WorkflowDefinition MapToModel(CreateWorkflowDefinitionRequest request) => new()
    {
        Name = request.Name,
        Description = request.Description,
        StartActivityId = request.StartActivityId,
        Activities = request.Activities.Select(a => new ActivityDefinition
        {
            Id = a.Id,
            Type = a.Type,
            Name = a.Name,
            Description = a.Description,
            Properties = a.Properties,
            InputMappings = a.InputMappings,
            OutputMappings = a.OutputMappings,
            Condition = a.Condition,
            Timeout = a.TimeoutSeconds.HasValue ? TimeSpan.FromSeconds(a.TimeoutSeconds.Value) : null,
            RetryPolicy = a.RetryPolicy is null ? null : new RetryPolicy
            {
                MaxAttempts = a.RetryPolicy.MaxAttempts,
                InitialDelay = TimeSpan.FromSeconds(a.RetryPolicy.InitialDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(a.RetryPolicy.MaxDelaySeconds),
                BackoffMultiplier = a.RetryPolicy.BackoffMultiplier
            }
        }).ToList(),
        Transitions = request.Transitions.Select(t => new TransitionDefinition
        {
            From = t.From,
            To = t.To,
            Condition = t.Condition,
            Priority = t.Priority,
            IsDefault = t.IsDefault
        }).ToList(),
        InputSchema = request.InputSchema is null ? null : new InputSchema
        {
            Properties = request.InputSchema.Properties.ToDictionary(
                p => p.Key,
                p => new PropertySchema
                {
                    Type = p.Value.Type,
                    Description = p.Value.Description,
                    Default = p.Value.Default
                }),
            Required = request.InputSchema.Required
        },
        OutputSchema = request.OutputSchema is null ? null : new OutputSchema
        {
            Properties = request.OutputSchema.Properties.ToDictionary(
                p => p.Key,
                p => new PropertySchema
                {
                    Type = p.Value.Type,
                    Description = p.Value.Description
                })
        },
        Trigger = request.Trigger is null ? null : new TriggerDefinition
        {
            Type = request.Trigger.Type,
            CronExpression = request.Trigger.CronExpression,
            EventType = request.Trigger.EventType,
            EventFilter = request.Trigger.EventFilter
        },
        DefaultRetryPolicy = request.DefaultRetryPolicy is null ? null : new RetryPolicy
        {
            MaxAttempts = request.DefaultRetryPolicy.MaxAttempts,
            InitialDelay = TimeSpan.FromSeconds(request.DefaultRetryPolicy.InitialDelaySeconds),
            MaxDelay = TimeSpan.FromSeconds(request.DefaultRetryPolicy.MaxDelaySeconds),
            BackoffMultiplier = request.DefaultRetryPolicy.BackoffMultiplier
        },
        Timeout = request.TimeoutSeconds.HasValue ? TimeSpan.FromSeconds(request.TimeoutSeconds.Value) : null,
        Tags = request.Tags ?? new(),
        IsActive = true
    };
}

/// <summary>
/// Result of cron expression validation.
/// </summary>
public class CronValidationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<DateTimeOffset>? NextOccurrences { get; set; }
}
