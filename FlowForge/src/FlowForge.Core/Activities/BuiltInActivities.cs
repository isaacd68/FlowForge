using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Activities;

/// <summary>
/// Activity that logs a message.
/// </summary>
public class LogActivity : ActivityBase
{
    private readonly ILogger<LogActivity> _logger;
    
    public LogActivity(ILogger<LogActivity> logger) => _logger = logger;
    
    public override string Type => "log";
    
    public override Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        var message = GetProperty<string>(context, "message");
        var level = GetPropertyOrDefault(context, "level", "Information");
        
        // Interpolate variables from input/state
        message = InterpolateMessage(message, context);
        
        var logLevel = Enum.Parse<LogLevel>(level, ignoreCase: true);
        _logger.Log(logLevel, "[Workflow {WorkflowId}] {Message}", context.Instance.Id, message);
        
        return Task.FromResult(ActivityResult.Ok(("logged", true)));
    }
    
    private static string InterpolateMessage(string template, ActivityContext context)
    {
        foreach (var (key, value) in context.Input)
        {
            template = template.Replace($"${{{key}}}", value?.ToString() ?? "");
        }
        foreach (var (key, value) in context.State)
        {
            template = template.Replace($"${{state.{key}}}", value?.ToString() ?? "");
        }
        return template;
    }
}

/// <summary>
/// Activity that delays execution.
/// </summary>
public class DelayActivity : ActivityBase
{
    public override string Type => "delay";
    
    public override async Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        var delayMs = GetProperty<int>(context, "delayMs");
        
        await Task.Delay(delayMs, context.CancellationToken);
        
        return ActivityResult.Ok(("delayed", delayMs));
    }
}

/// <summary>
/// Activity that makes HTTP requests.
/// </summary>
public class HttpActivity : ActivityBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    
    public HttpActivity(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;
    
    public override string Type => "http";
    
    public override async Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        var url = GetProperty<string>(context, "url");
        var method = GetPropertyOrDefault(context, "method", "GET");
        var headers = GetPropertyOrDefault<Dictionary<string, string>>(context, "headers");
        var body = GetPropertyOrDefault<object>(context, "body");
        var timeoutSeconds = GetPropertyOrDefault(context, "timeoutSeconds", 30);
        
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }
        
        if (body is not null && method is not "GET" and not "HEAD")
        {
            request.Content = JsonContent.Create(body);
        }
        
        try
        {
            var response = await client.SendAsync(request, context.CancellationToken);
            var content = await response.Content.ReadAsStringAsync(context.CancellationToken);
            
            object? responseBody = null;
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                try
                {
                    responseBody = JsonSerializer.Deserialize<JsonElement>(content);
                }
                catch
                {
                    responseBody = content;
                }
            }
            else
            {
                responseBody = content;
            }
            
            if (!response.IsSuccessStatusCode)
            {
                return ActivityResult.Fail(
                    $"HTTP_{(int)response.StatusCode}",
                    $"HTTP request failed with status {response.StatusCode}");
            }
            
            return ActivityResult.Ok(new Dictionary<string, object?>
            {
                ["statusCode"] = (int)response.StatusCode,
                ["body"] = responseBody,
                ["headers"] = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value))
            });
        }
        catch (TaskCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return ActivityResult.Fail("TIMEOUT", "HTTP request timed out");
        }
        catch (HttpRequestException ex)
        {
            return ActivityResult.Fail("HTTP_ERROR", ex.Message, ex);
        }
    }
}

/// <summary>
/// Activity that transforms data using expressions.
/// </summary>
public class TransformActivity : ActivityBase
{
    public override string Type => "transform";
    
    public override Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        var mappings = GetProperty<Dictionary<string, string>>(context, "mappings");
        var output = new Dictionary<string, object?>();
        
        foreach (var (outputKey, expression) in mappings)
        {
            var value = EvaluateExpression(expression, context);
            output[outputKey] = value;
        }
        
        return Task.FromResult(ActivityResult.Ok(output));
    }
    
    private static object? EvaluateExpression(string expression, ActivityContext context)
    {
        // Simple expression evaluation - supports input.x, state.x, literals
        expression = expression.Trim();
        
        if (expression.StartsWith("input."))
        {
            var key = expression[6..];
            return context.Input.GetValueOrDefault(key);
        }
        
        if (expression.StartsWith("state."))
        {
            var key = expression[6..];
            return context.State.GetValueOrDefault(key);
        }
        
        // Check for string literal
        if (expression.StartsWith('"') && expression.EndsWith('"'))
        {
            return expression[1..^1];
        }
        
        // Check for number
        if (double.TryParse(expression, out var number))
        {
            return number;
        }
        
        // Check for boolean
        if (bool.TryParse(expression, out var boolean))
        {
            return boolean;
        }
        
        return expression;
    }
}

/// <summary>
/// Activity that executes conditional logic.
/// </summary>
public class ConditionActivity : ActivityBase
{
    public override string Type => "condition";
    
    public override Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        var conditions = GetProperty<List<ConditionBranch>>(context, "conditions");
        
        foreach (var branch in conditions)
        {
            if (EvaluateCondition(branch.Expression, context))
            {
                return Task.FromResult(new ActivityResult
                {
                    Success = true,
                    Output = new Dictionary<string, object?> { ["branch"] = branch.Name },
                    NextActivityId = branch.NextActivityId
                });
            }
        }
        
        // Default branch
        var defaultNext = GetPropertyOrDefault<string>(context, "defaultNextActivity");
        return Task.FromResult(new ActivityResult
        {
            Success = true,
            Output = new Dictionary<string, object?> { ["branch"] = "default" },
            NextActivityId = defaultNext
        });
    }
    
    private static bool EvaluateCondition(string expression, ActivityContext context)
    {
        // Simple condition evaluation
        // Supports: ==, !=, >, <, >=, <=, contains, startsWith, endsWith
        
        var parts = expression.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;
        
        var leftValue = ResolveValue(parts[0], context);
        var op = parts[1];
        var rightValue = ResolveValue(parts[2], context);
        
        return op switch
        {
            "==" => Equals(leftValue, rightValue),
            "!=" => !Equals(leftValue, rightValue),
            ">" => Compare(leftValue, rightValue) > 0,
            "<" => Compare(leftValue, rightValue) < 0,
            ">=" => Compare(leftValue, rightValue) >= 0,
            "<=" => Compare(leftValue, rightValue) <= 0,
            "contains" => leftValue?.ToString()?.Contains(rightValue?.ToString() ?? "") ?? false,
            "startsWith" => leftValue?.ToString()?.StartsWith(rightValue?.ToString() ?? "") ?? false,
            "endsWith" => leftValue?.ToString()?.EndsWith(rightValue?.ToString() ?? "") ?? false,
            _ => false
        };
    }
    
    private static object? ResolveValue(string expr, ActivityContext context)
    {
        if (expr.StartsWith("input."))
            return context.Input.GetValueOrDefault(expr[6..]);
        if (expr.StartsWith("state."))
            return context.State.GetValueOrDefault(expr[6..]);
        if (expr.StartsWith('"') && expr.EndsWith('"'))
            return expr[1..^1];
        if (double.TryParse(expr, out var num))
            return num;
        if (bool.TryParse(expr, out var b))
            return b;
        return expr;
    }
    
    private static int Compare(object? left, object? right)
    {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        
        if (left is IComparable comparable)
        {
            try
            {
                var rightConverted = Convert.ChangeType(right, left.GetType());
                return comparable.CompareTo(rightConverted);
            }
            catch
            {
                return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
            }
        }
        
        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>
/// Condition branch definition.
/// </summary>
public class ConditionBranch
{
    public required string Name { get; set; }
    public required string Expression { get; set; }
    public required string NextActivityId { get; set; }
}

/// <summary>
/// Activity that iterates over a collection.
/// </summary>
public class ForEachActivity : ActivityBase
{
    public override string Type => "foreach";
    
    public override Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        var collectionExpr = GetProperty<string>(context, "collection");
        var itemVariable = GetPropertyOrDefault(context, "itemVariable", "item");
        var indexVariable = GetPropertyOrDefault(context, "indexVariable", "index");
        
        // Resolve collection
        object? collection = null;
        if (collectionExpr.StartsWith("input."))
            collection = context.Input.GetValueOrDefault(collectionExpr[6..]);
        else if (collectionExpr.StartsWith("state."))
            collection = context.State.GetValueOrDefault(collectionExpr[6..]);
        
        if (collection is not IEnumerable<object> items)
        {
            if (collection is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                items = element.EnumerateArray().Select(e => (object)e).ToList();
            }
            else
            {
                return Task.FromResult(ActivityResult.Fail("INVALID_COLLECTION", "Collection is not iterable"));
            }
        }
        
        var results = new List<object?>();
        var index = 0;
        
        foreach (var item in items)
        {
            context.State[$"_foreach_{itemVariable}"] = item;
            context.State[$"_foreach_{indexVariable}"] = index;
            results.Add(item);
            index++;
        }
        
        return Task.FromResult(ActivityResult.Ok(new Dictionary<string, object?>
        {
            ["items"] = results,
            ["count"] = index
        }));
    }
}

/// <summary>
/// Activity that sets values in workflow state.
/// </summary>
public class SetStateActivity : ActivityBase
{
    public override string Type => "setState";
    
    public override Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        var values = GetProperty<Dictionary<string, object?>>(context, "values");
        
        foreach (var (key, value) in values)
        {
            context.State[key] = value;
        }
        
        return Task.FromResult(ActivityResult.Ok(values));
    }
}

/// <summary>
/// Activity that suspends workflow and waits for external signal.
/// </summary>
public class WaitForSignalActivity : ActivityBase
{
    public override string Type => "waitForSignal";
    
    public override Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        var signalName = GetProperty<string>(context, "signalName");
        var timeoutSeconds = GetPropertyOrDefault<int?>(context, "timeoutSeconds");
        
        return Task.FromResult(ActivityResult.SuspendFor(signalName));
    }
}

/// <summary>
/// Activity that invokes a child workflow.
/// </summary>
public class InvokeWorkflowActivity : ActivityBase
{
    public override string Type => "invokeWorkflow";
    
    public override Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        // This activity is handled specially by the workflow engine
        // to create and execute a child workflow
        
        var workflowName = GetProperty<string>(context, "workflowName");
        var input = GetPropertyOrDefault<Dictionary<string, object?>>(context, "input");
        var waitForCompletion = GetPropertyOrDefault(context, "waitForCompletion", true);
        
        // The engine will handle the actual invocation
        return Task.FromResult(ActivityResult.Ok(new Dictionary<string, object?>
        {
            ["childWorkflowName"] = workflowName,
            ["childWorkflowInput"] = input,
            ["waitForCompletion"] = waitForCompletion
        }));
    }
}

/// <summary>
/// Activity that runs activities in parallel.
/// </summary>
public class ParallelActivity : ActivityBase
{
    public override string Type => "parallel";
    
    public override Task<ActivityResult> ExecuteAsync(ActivityContext context)
    {
        var branches = GetProperty<List<string>>(context, "branches");
        var waitAll = GetPropertyOrDefault(context, "waitAll", true);
        
        // The engine handles parallel execution
        return Task.FromResult(ActivityResult.Ok(new Dictionary<string, object?>
        {
            ["branches"] = branches,
            ["waitAll"] = waitAll
        }));
    }
}
