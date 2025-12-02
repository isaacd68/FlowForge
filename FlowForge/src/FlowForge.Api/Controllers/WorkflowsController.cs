using FlowForge.Api.Hubs;
using FlowForge.Core.Workflows;
using FlowForge.Shared.Constants;
using FlowForge.Shared.Contracts;
using FlowForge.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace FlowForge.Api.Controllers;

/// <summary>
/// API endpoints for workflow instance management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class WorkflowsController : ControllerBase
{
    private readonly WorkflowEngine _engine;
    private readonly IWorkflowInstanceRepository _instanceRepository;
    private readonly IActivityExecutionRepository _executionRepository;
    private readonly IHubContext<WorkflowHub, IWorkflowHubClient> _hubContext;
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(
        WorkflowEngine engine,
        IWorkflowInstanceRepository instanceRepository,
        IActivityExecutionRepository executionRepository,
        IHubContext<WorkflowHub, IWorkflowHubClient> hubContext,
        ILogger<WorkflowsController> logger)
    {
        _engine = engine;
        _instanceRepository = instanceRepository;
        _executionRepository = executionRepository;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Start a new workflow instance.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(StartWorkflowResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StartWorkflowResponse>> StartWorkflow(
        [FromBody] StartWorkflowRequest request,
        CancellationToken ct)
    {
        var result = await _engine.StartWorkflowAsync(
            request.WorkflowName,
            request.Input,
            request.CorrelationId,
            ct: ct);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "WORKFLOW_NOT_FOUND" => NotFound(new ErrorResponse 
                { 
                    Error = result.ErrorCode, 
                    Message = result.ErrorMessage! 
                }),
                _ => BadRequest(new ErrorResponse 
                { 
                    Error = result.ErrorCode!, 
                    Message = result.ErrorMessage! 
                })
            };
        }

        var instance = result.Instance!;

        // Notify connected clients
        await _hubContext.Clients.All.WorkflowStarted(new WorkflowEventDto
        {
            InstanceId = instance.Id,
            WorkflowName = instance.WorkflowName,
            Status = instance.Status.ToString(),
            Timestamp = instance.CreatedAt
        });

        // Queue execution (in production, this would go to a message queue)
        _ = Task.Run(async () =>
        {
            var execResult = await _engine.ExecuteAsync(instance.Id);
            if (execResult.Instance is not null)
            {
                await _hubContext.Clients.All.WorkflowUpdated(new WorkflowEventDto
                {
                    InstanceId = execResult.Instance.Id,
                    WorkflowName = execResult.Instance.WorkflowName,
                    Status = execResult.Instance.Status.ToString(),
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
        }, CancellationToken.None);

        var response = new StartWorkflowResponse
        {
            InstanceId = instance.Id,
            Status = instance.Status,
            CreatedAt = instance.CreatedAt
        };

        return CreatedAtAction(nameof(GetWorkflowInstance), new { id = instance.Id }, response);
    }

    /// <summary>
    /// Get a workflow instance by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkflowInstanceDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowInstanceDetail>> GetWorkflowInstance(Guid id, CancellationToken ct)
    {
        var instance = await _instanceRepository.GetAsync(id, ct);
        if (instance is null)
            return NotFound();

        var executions = await _executionRepository.GetByInstanceAsync(id, ct);

        return new WorkflowInstanceDetail
        {
            Id = instance.Id,
            WorkflowName = instance.WorkflowName,
            WorkflowVersion = instance.WorkflowVersion,
            Status = instance.Status,
            Input = instance.Input,
            Output = instance.Output,
            State = instance.State,
            CurrentActivityId = instance.CurrentActivityId,
            ParentInstanceId = instance.ParentInstanceId,
            CorrelationId = instance.CorrelationId,
            Error = instance.Error is null ? null : new WorkflowErrorDto
            {
                Code = instance.Error.Code,
                Message = instance.Error.Message,
                ActivityId = instance.Error.ActivityId,
                StackTrace = instance.Error.StackTrace,
                OccurredAt = instance.Error.OccurredAt
            },
            CreatedAt = instance.CreatedAt,
            StartedAt = instance.StartedAt,
            CompletedAt = instance.CompletedAt,
            DurationMs = instance.StartedAt.HasValue && instance.CompletedAt.HasValue
                ? (long)(instance.CompletedAt.Value - instance.StartedAt.Value).TotalMilliseconds
                : null,
            RetryCount = instance.RetryCount,
            WorkerId = instance.WorkerId,
            Tags = instance.Tags,
            Metadata = instance.Metadata,
            ActivityHistory = executions.Select(e => new ActivityExecutionDto
            {
                Id = e.Id,
                ActivityId = e.ActivityId,
                ActivityType = e.ActivityType,
                Status = e.Status,
                Attempt = e.Attempt,
                StartedAt = e.StartedAt,
                CompletedAt = e.CompletedAt,
                DurationMs = e.DurationMs,
                Error = e.Error is null ? null : new WorkflowErrorDto
                {
                    Code = e.Error.Code,
                    Message = e.Error.Message,
                    ActivityId = e.Error.ActivityId,
                    OccurredAt = e.Error.OccurredAt
                }
            }).ToList()
        };
    }

    /// <summary>
    /// List workflow instances with filtering and pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<WorkflowInstanceSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<WorkflowInstanceSummary>>> ListWorkflowInstances(
        [FromQuery] WorkflowInstanceQuery query,
        CancellationToken ct)
    {
        var result = await _instanceRepository.QueryAsync(query, ct);

        return new PagedResult<WorkflowInstanceSummary>
        {
            Items = result.Items.Select(i => new WorkflowInstanceSummary
            {
                Id = i.Id,
                WorkflowName = i.WorkflowName,
                WorkflowVersion = i.WorkflowVersion,
                Status = i.Status,
                CurrentActivityId = i.CurrentActivityId,
                CreatedAt = i.CreatedAt,
                StartedAt = i.StartedAt,
                CompletedAt = i.CompletedAt,
                DurationMs = i.StartedAt.HasValue && i.CompletedAt.HasValue
                    ? (long)(i.CompletedAt.Value - i.StartedAt.Value).TotalMilliseconds
                    : null,
                ErrorMessage = i.Error?.Message,
                Tags = i.Tags
            }).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
    }

    /// <summary>
    /// Cancel a running workflow.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelWorkflow(Guid id, CancellationToken ct)
    {
        var result = await _engine.CancelAsync(id, ct);
        
        if (!result.Success)
            return NotFound();

        await _hubContext.Clients.All.WorkflowUpdated(new WorkflowEventDto
        {
            InstanceId = id,
            WorkflowName = result.Instance!.WorkflowName,
            Status = WorkflowStatus.Cancelled.ToString(),
            Timestamp = DateTimeOffset.UtcNow
        });

        return Ok();
    }

    /// <summary>
    /// Resume a suspended workflow with a signal.
    /// </summary>
    [HttpPost("{id:guid}/signal/{signalName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendSignal(
        Guid id,
        string signalName,
        [FromBody] Dictionary<string, object?>? data,
        CancellationToken ct)
    {
        var result = await _engine.ResumeWithSignalAsync(id, signalName, data, ct);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "INSTANCE_NOT_FOUND" => NotFound(),
                _ => BadRequest(new ErrorResponse 
                { 
                    Error = result.ErrorCode!, 
                    Message = result.ErrorMessage! 
                })
            };
        }

        return Ok();
    }

    /// <summary>
    /// Delete a workflow instance.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWorkflow(Guid id, CancellationToken ct)
    {
        var deleted = await _instanceRepository.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Get dashboard statistics.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(DashboardStats), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardStats>> GetStats(CancellationToken ct)
    {
        return await _instanceRepository.GetStatsAsync(ct);
    }
}

/// <summary>
/// Error response model.
/// </summary>
public class ErrorResponse
{
    public required string Error { get; set; }
    public required string Message { get; set; }
}
