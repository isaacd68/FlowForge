using FlowForge.Core.Expressions;
using FlowForge.Shared.Models;
using FluentAssertions;
using Xunit;

namespace FlowForge.Core.Tests.Expressions;

public class ExpressionEvaluatorTests
{
    private readonly JintExpressionEvaluator _evaluator = new();

    private static WorkflowInstance CreateInstance(
        Dictionary<string, object?>? input = null,
        Dictionary<string, object?>? state = null)
    {
        return new WorkflowInstance
        {
            WorkflowName = "test",
            Input = input ?? new(),
            State = state ?? new()
        };
    }

    [Fact]
    public void Evaluate_SimpleArithmetic_ShouldCompute()
    {
        var instance = CreateInstance();
        
        var result = _evaluator.Evaluate("2 + 3 * 4", instance);
        
        result.Should().Be(14);
    }

    [Fact]
    public void Evaluate_StateAccess_ShouldReturnValue()
    {
        var instance = CreateInstance(state: new()
        {
            ["userName"] = "Alice",
            ["orderCount"] = 5
        });
        
        var name = _evaluator.Evaluate("state.userName", instance);
        var count = _evaluator.Evaluate("state.orderCount", instance);
        
        name.Should().Be("Alice");
        count.Should().Be(5);
    }

    [Fact]
    public void Evaluate_InputAccess_ShouldReturnValue()
    {
        var instance = CreateInstance(input: new()
        {
            ["customerId"] = "C123",
            ["amount"] = 99.99
        });
        
        var customerId = _evaluator.Evaluate("input.customerId", instance);
        var amount = _evaluator.Evaluate("input.amount", instance);
        
        customerId.Should().Be("C123");
        amount.Should().Be(99.99);
    }

    [Fact]
    public void EvaluateCondition_Comparison_ShouldReturnBoolean()
    {
        var instance = CreateInstance(state: new()
        {
            ["count"] = 10,
            ["status"] = "active"
        });
        
        _evaluator.EvaluateCondition("state.count > 5", instance).Should().BeTrue();
        _evaluator.EvaluateCondition("state.count < 5", instance).Should().BeFalse();
        _evaluator.EvaluateCondition("state.status === 'active'", instance).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_StringOperations_ShouldWork()
    {
        var instance = CreateInstance(state: new()
        {
            ["text"] = "Hello World"
        });
        
        _evaluator.Evaluate("state.text.toLowerCase()", instance).Should().Be("hello world");
        _evaluator.Evaluate("state.text.toUpperCase()", instance).Should().Be("HELLO WORLD");
        _evaluator.Evaluate("state.text.includes('World')", instance).Should().Be(true);
    }

    [Fact]
    public void Evaluate_UtilityFunctions_ShouldWork()
    {
        var instance = CreateInstance();
        
        var uuid = _evaluator.Evaluate("uuid()", instance)?.ToString();
        uuid.Should().NotBeNullOrEmpty();
        Guid.TryParse(uuid, out _).Should().BeTrue();
        
        var timestamp = _evaluator.Evaluate("now()", instance)?.ToString();
        timestamp.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Evaluate_MathFunctions_ShouldWork()
    {
        var instance = CreateInstance();
        
        _evaluator.Evaluate("round(3.7, 0)", instance).Should().Be(4.0);
        _evaluator.Evaluate("floor(3.7)", instance).Should().Be(3.0);
        _evaluator.Evaluate("ceil(3.2)", instance).Should().Be(4.0);
        _evaluator.Evaluate("abs(-5)", instance).Should().Be(5.0);
        _evaluator.Evaluate("min(3, 7)", instance).Should().Be(3.0);
        _evaluator.Evaluate("max(3, 7)", instance).Should().Be(7.0);
    }

    [Fact]
    public void Evaluate_ArrayOperations_ShouldWork()
    {
        var instance = CreateInstance(state: new()
        {
            ["items"] = new List<object> { 1, 2, 3, 4, 5 }
        });
        
        _evaluator.Evaluate("length(state.items)", instance).Should().Be(5);
        _evaluator.Evaluate("first(state.items)", instance).Should().Be(1);
        _evaluator.Evaluate("last(state.items)", instance).Should().Be(5);
    }

    [Fact]
    public void Evaluate_NullHandling_ShouldWork()
    {
        var instance = CreateInstance(state: new()
        {
            ["value"] = null,
            ["other"] = "exists"
        });
        
        _evaluator.Evaluate("coalesce(state.value, 'default')", instance).Should().Be("default");
        _evaluator.Evaluate("coalesce(state.other, 'default')", instance).Should().Be("exists");
        _evaluator.Evaluate("isEmpty(state.value)", instance).Should().Be(true);
        _evaluator.Evaluate("isEmpty(state.other)", instance).Should().Be(false);
    }

    [Fact]
    public void Evaluate_ComplexExpression_ShouldWork()
    {
        var instance = CreateInstance(
            input: new() { ["quantity"] = 5, ["price"] = 19.99 },
            state: new() { ["discount"] = 0.1 });
        
        var result = _evaluator.Evaluate(
            "input.quantity * input.price * (1 - state.discount)", 
            instance);
        
        result.Should().Be(89.955);
    }

    [Fact]
    public void Transform_ShouldCreateObject()
    {
        var input = new Dictionary<string, object?>
        {
            ["firstName"] = "John",
            ["lastName"] = "Doe",
            ["age"] = 30
        };
        
        var result = _evaluator.Transform(
            "({ fullName: $.firstName + ' ' + $.lastName, isAdult: $.age >= 18 })",
            input);
        
        result["fullName"].Should().Be("John Doe");
        result["isAdult"].Should().Be(true);
    }

    [Fact]
    public void Evaluate_InvalidExpression_ShouldThrow()
    {
        var instance = CreateInstance();
        
        Action act = () => _evaluator.Evaluate("invalid syntax {{", instance);
        
        act.Should().Throw<ExpressionEvaluationException>();
    }

    [Fact]
    public void Evaluate_WorkflowMetadata_ShouldBeAccessible()
    {
        var instance = CreateInstance();
        instance.Id = Guid.NewGuid();
        instance.CorrelationId = "corr-123";
        
        var result = _evaluator.Evaluate("workflow.id", instance);
        result.Should().Be(instance.Id.ToString());
        
        var corr = _evaluator.Evaluate("workflow.correlationId", instance);
        corr.Should().Be("corr-123");
    }
}

public class SimpleExpressionEvaluatorTests
{
    [Fact]
    public void EvaluatePath_StatePath_ShouldReturnValue()
    {
        var instance = new WorkflowInstance
        {
            WorkflowName = "test",
            State = new() { ["user"] = new Dictionary<string, object?> { ["name"] = "Alice" } }
        };
        
        // Note: SimpleExpressionEvaluator handles nested paths
        var result = SimpleExpressionEvaluator.EvaluatePath("state.user", instance);
        
        result.Should().NotBeNull();
    }

    [Fact]
    public void Interpolate_Template_ShouldReplaceVariables()
    {
        var instance = new WorkflowInstance
        {
            WorkflowName = "test",
            Input = new() { ["orderId"] = "ORD-123" },
            State = new() { ["status"] = "processing" }
        };
        
        var result = SimpleExpressionEvaluator.Interpolate(
            "Order ${input.orderId} is ${state.status}", 
            instance);
        
        result.Should().Be("Order ORD-123 is processing");
    }

    [Fact]
    public void Interpolate_MissingVariable_ShouldReturnEmpty()
    {
        var instance = new WorkflowInstance
        {
            WorkflowName = "test"
        };
        
        var result = SimpleExpressionEvaluator.Interpolate(
            "Value: ${state.missing}", 
            instance);
        
        result.Should().Be("Value: ");
    }
}
