using System.Text.Json;
using Jint;
using Jint.Native;
using FlowForge.Shared.Models;

namespace FlowForge.Core.Expressions;

/// <summary>
/// Evaluates JavaScript expressions within workflow context.
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>Evaluate an expression and return the result.</summary>
    object? Evaluate(string expression, WorkflowInstance instance);
    
    /// <summary>Evaluate a boolean condition.</summary>
    bool EvaluateCondition(string expression, WorkflowInstance instance);
    
    /// <summary>Transform input data using an expression.</summary>
    Dictionary<string, object?> Transform(string expression, Dictionary<string, object?> input);
}

/// <summary>
/// JavaScript expression evaluator using Jint engine.
/// </summary>
public class JintExpressionEvaluator : IExpressionEvaluator
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);
    private readonly int _memoryLimit = 4_000_000;

    /// <inheritdoc/>
    public object? Evaluate(string expression, WorkflowInstance instance)
    {
        var engine = CreateEngine();
        
        // Set up context
        SetupContext(engine, instance);
        
        try
        {
            var result = engine.Evaluate(expression);
            return ConvertResult(result);
        }
        catch (Jint.Runtime.JavaScriptException ex)
        {
            throw new ExpressionEvaluationException($"Expression error: {ex.Message}", ex);
        }
        catch (TimeoutException)
        {
            throw new ExpressionEvaluationException("Expression evaluation timed out");
        }
    }

    /// <inheritdoc/>
    public bool EvaluateCondition(string expression, WorkflowInstance instance)
    {
        var result = Evaluate(expression, instance);
        
        return result switch
        {
            bool b => b,
            int i => i != 0,
            double d => d != 0,
            string s => !string.IsNullOrEmpty(s),
            null => false,
            _ => true
        };
    }

    /// <inheritdoc/>
    public Dictionary<string, object?> Transform(string expression, Dictionary<string, object?> input)
    {
        var engine = CreateEngine();
        
        // Set input
        var inputObj = JsValue.FromObject(engine, input);
        engine.SetValue("input", inputObj);
        engine.SetValue("$", inputObj);
        
        try
        {
            var result = engine.Evaluate(expression);
            
            if (result.IsObject())
            {
                var obj = result.AsObject();
                var dict = new Dictionary<string, object?>();
                
                foreach (var prop in obj.GetOwnProperties())
                {
                    dict[prop.Key.AsString()] = ConvertResult(obj.Get(prop.Key));
                }
                
                return dict;
            }
            
            throw new ExpressionEvaluationException("Transform expression must return an object");
        }
        catch (Jint.Runtime.JavaScriptException ex)
        {
            throw new ExpressionEvaluationException($"Transform error: {ex.Message}", ex);
        }
    }

    private Engine CreateEngine()
    {
        return new Engine(options =>
        {
            options.TimeoutInterval(_timeout);
            options.LimitMemory(_memoryLimit);
            options.LimitRecursion(100);
            options.Strict(true);
            options.CatchClrExceptions();
        });
    }

    private void SetupContext(Engine engine, WorkflowInstance instance)
    {
        // Workflow state and input
        engine.SetValue("state", JsValue.FromObject(engine, instance.State));
        engine.SetValue("input", JsValue.FromObject(engine, instance.Input));
        engine.SetValue("output", JsValue.FromObject(engine, instance.Output));
        
        // Shorthand accessors
        engine.SetValue("$state", JsValue.FromObject(engine, instance.State));
        engine.SetValue("$input", JsValue.FromObject(engine, instance.Input));
        
        // Instance metadata
        engine.SetValue("workflow", new
        {
            id = instance.Id.ToString(),
            name = instance.WorkflowName,
            version = instance.WorkflowVersion,
            correlationId = instance.CorrelationId ?? "",
            status = instance.Status.ToString(),
            createdAt = instance.CreatedAt.ToString("O"),
            startedAt = instance.StartedAt?.ToString("O") ?? ""
        });
        
        // Utility functions
        engine.SetValue("now", new Func<string>(() => DateTimeOffset.UtcNow.ToString("O")));
        engine.SetValue("uuid", new Func<string>(() => Guid.NewGuid().ToString()));
        engine.SetValue("json", new Func<object, string>(obj => JsonSerializer.Serialize(obj)));
        engine.SetValue("parse", new Func<string, object?>(s => 
        {
            try { return JsonSerializer.Deserialize<JsonElement>(s); }
            catch { return null; }
        }));
        
        // String utilities
        engine.SetValue("lower", new Func<string, string>(s => s?.ToLowerInvariant() ?? ""));
        engine.SetValue("upper", new Func<string, string>(s => s?.ToUpperInvariant() ?? ""));
        engine.SetValue("trim", new Func<string, string>(s => s?.Trim() ?? ""));
        engine.SetValue("contains", new Func<string, string, bool>((s, sub) => s?.Contains(sub) ?? false));
        engine.SetValue("startsWith", new Func<string, string, bool>((s, prefix) => s?.StartsWith(prefix) ?? false));
        engine.SetValue("endsWith", new Func<string, string, bool>((s, suffix) => s?.EndsWith(suffix) ?? false));
        
        // Math utilities
        engine.SetValue("round", new Func<double, int, double>((n, d) => Math.Round(n, d)));
        engine.SetValue("floor", new Func<double, double>(n => Math.Floor(n)));
        engine.SetValue("ceil", new Func<double, double>(n => Math.Ceiling(n)));
        engine.SetValue("abs", new Func<double, double>(n => Math.Abs(n)));
        engine.SetValue("min", new Func<double, double, double>((a, b) => Math.Min(a, b)));
        engine.SetValue("max", new Func<double, double, double>((a, b) => Math.Max(a, b)));
        
        // Array utilities
        engine.SetValue("length", new Func<object, int>(obj =>
        {
            if (obj is string s) return s.Length;
            if (obj is System.Collections.ICollection c) return c.Count;
            if (obj is System.Collections.IEnumerable e) return e.Cast<object>().Count();
            return 0;
        }));
        
        engine.SetValue("first", new Func<object, object?>(obj =>
        {
            if (obj is System.Collections.IEnumerable e)
                return e.Cast<object>().FirstOrDefault();
            return null;
        }));
        
        engine.SetValue("last", new Func<object, object?>(obj =>
        {
            if (obj is System.Collections.IEnumerable e)
                return e.Cast<object>().LastOrDefault();
            return null;
        }));
        
        // Null coalescing
        engine.SetValue("coalesce", new Func<object?, object?, object?>((a, b) => a ?? b));
        engine.SetValue("isEmpty", new Func<object?, bool>(obj =>
        {
            if (obj is null) return true;
            if (obj is string s) return string.IsNullOrWhiteSpace(s);
            if (obj is System.Collections.ICollection c) return c.Count == 0;
            return false;
        }));
    }

    private static object? ConvertResult(JsValue value)
    {
        if (value.IsNull() || value.IsUndefined())
            return null;
        
        if (value.IsBoolean())
            return value.AsBoolean();
        
        if (value.IsNumber())
        {
            var num = value.AsNumber();
            if (num == Math.Floor(num) && num >= int.MinValue && num <= int.MaxValue)
                return (int)num;
            return num;
        }
        
        if (value.IsString())
            return value.AsString();
        
        if (value.IsArray())
        {
            var arr = value.AsArray();
            var list = new List<object?>();
            for (uint i = 0; i < arr.Length; i++)
            {
                list.Add(ConvertResult(arr.Get(i)));
            }
            return list;
        }
        
        if (value.IsObject())
        {
            var obj = value.AsObject();
            var dict = new Dictionary<string, object?>();
            foreach (var prop in obj.GetOwnProperties())
            {
                dict[prop.Key.AsString()] = ConvertResult(obj.Get(prop.Key));
            }
            return dict;
        }
        
        return value.ToString();
    }
}

/// <summary>
/// Exception thrown when expression evaluation fails.
/// </summary>
public class ExpressionEvaluationException : Exception
{
    public ExpressionEvaluationException(string message) : base(message) { }
    public ExpressionEvaluationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Simple expression evaluator for basic operations without JS engine.
/// </summary>
public class SimpleExpressionEvaluator
{
    /// <summary>
    /// Evaluate a simple path expression like "state.user.name" or "input.orderId".
    /// </summary>
    public static object? EvaluatePath(string path, WorkflowInstance instance)
    {
        var parts = path.Split('.');
        if (parts.Length == 0) return null;
        
        object? current = parts[0].ToLower() switch
        {
            "state" => instance.State,
            "input" => instance.Input,
            "output" => instance.Output,
            _ => null
        };
        
        if (current is null) return null;
        
        for (int i = 1; i < parts.Length; i++)
        {
            if (current is Dictionary<string, object?> dict)
            {
                if (!dict.TryGetValue(parts[i], out current))
                    return null;
            }
            else if (current is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(parts[i], out var prop))
                {
                    current = prop;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }
        
        // Convert JsonElement to proper type
        if (current is JsonElement je)
        {
            current = je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => je
            };
        }
        
        return current;
    }
    
    /// <summary>
    /// Interpolate a template string with values from instance context.
    /// Format: "Hello ${state.userName}, order ${input.orderId} received"
    /// </summary>
    public static string Interpolate(string template, WorkflowInstance instance)
    {
        var result = template;
        var start = 0;
        
        while (true)
        {
            var openIndex = result.IndexOf("${", start, StringComparison.Ordinal);
            if (openIndex < 0) break;
            
            var closeIndex = result.IndexOf("}", openIndex, StringComparison.Ordinal);
            if (closeIndex < 0) break;
            
            var path = result.Substring(openIndex + 2, closeIndex - openIndex - 2);
            var value = EvaluatePath(path, instance);
            
            result = result[..openIndex] + (value?.ToString() ?? "") + result[(closeIndex + 1)..];
            start = openIndex + (value?.ToString()?.Length ?? 0);
        }
        
        return result;
    }
}
