using FlowForge.Core.Workflows;
using FluentAssertions;
using Xunit;

namespace FlowForge.Core.Tests.Workflows;

public class WorkflowBuilderTests
{
    [Fact]
    public void Create_WithValidName_ShouldCreateBuilder()
    {
        // Act
        var builder = WorkflowBuilder.Create("test-workflow");
        
        // Assert
        builder.Should().NotBeNull();
    }

    [Fact]
    public void Build_WithMinimalConfiguration_ShouldCreateDefinition()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("simple-workflow")
            .WithDescription("A simple test workflow")
            .AddLog("start", "Workflow started")
            .Build();

        // Assert
        definition.Name.Should().Be("simple-workflow");
        definition.Description.Should().Be("A simple test workflow");
        definition.Activities.Should().HaveCount(1);
        definition.StartActivityId.Should().Be("start");
    }

    [Fact]
    public void Build_WithMultipleActivities_ShouldChainCorrectly()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("multi-step")
            .AddLog("step1", "Step 1")
            .Then("step2")
            .AddLog("step2", "Step 2")
            .Then("step3")
            .AddLog("step3", "Step 3")
            .Build();

        // Assert
        definition.Activities.Should().HaveCount(3);
        definition.Transitions.Should().HaveCount(2);
        definition.Transitions[0].From.Should().Be("step1");
        definition.Transitions[0].To.Should().Be("step2");
        definition.Transitions[1].From.Should().Be("step2");
        definition.Transitions[1].To.Should().Be("step3");
    }

    [Fact]
    public void Build_WithConditionalBranching_ShouldSetupTransitions()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("conditional-workflow")
            .AddActivity("check", "condition", a => a
                .WithProperty("conditions", new List<object>
                {
                    new { Name = "high", Expression = "input.value > 100", NextActivityId = "process-high" },
                    new { Name = "low", Expression = "input.value <= 100", NextActivityId = "process-low" }
                }))
            .AddLog("process-high", "Processing high value")
            .AddLog("process-low", "Processing low value")
            .AddTransition("check", "process-high", "state.branch == \"high\"")
            .AddTransition("check", "process-low", isDefault: true)
            .Build();

        // Assert
        definition.Activities.Should().HaveCount(3);
        definition.Transitions.Should().HaveCount(2);
        definition.Transitions.Should().Contain(t => t.IsDefault);
    }

    [Fact]
    public void Build_WithInputSchema_ShouldValidateSchema()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("validated-workflow")
            .WithInput(schema => schema
                .AddString("name", required: true, description: "Customer name")
                .AddNumber("amount", required: true, description: "Order amount")
                .AddBoolean("priority", description: "Priority flag"))
            .AddLog("start", "Processing ${input.name}")
            .Build();

        // Assert
        definition.InputSchema.Should().NotBeNull();
        definition.InputSchema!.Properties.Should().HaveCount(3);
        definition.InputSchema.Required.Should().Contain("name");
        definition.InputSchema.Required.Should().Contain("amount");
    }

    [Fact]
    public void Build_WithRetryPolicy_ShouldConfigureRetries()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("retry-workflow")
            .WithDefaultRetryPolicy(policy => policy
                .MaxAttempts(5)
                .InitialDelay(TimeSpan.FromSeconds(2))
                .BackoffMultiplier(2.5))
            .AddActivity("unreliable", "http", a => a
                .WithProperty("url", "https://api.example.com/data")
                .WithRetryPolicy(p => p
                    .MaxAttempts(3)
                    .InitialDelay(TimeSpan.FromMilliseconds(500))))
            .Build();

        // Assert
        definition.DefaultRetryPolicy.Should().NotBeNull();
        definition.DefaultRetryPolicy!.MaxAttempts.Should().Be(5);
        definition.DefaultRetryPolicy.BackoffMultiplier.Should().Be(2.5);
        
        definition.Activities[0].RetryPolicy.Should().NotBeNull();
        definition.Activities[0].RetryPolicy!.MaxAttempts.Should().Be(3);
    }

    [Fact]
    public void Build_WithScheduledTrigger_ShouldConfigureCron()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("scheduled-workflow")
            .WithScheduledTrigger("0 */5 * * * *") // Every 5 minutes
            .AddLog("execute", "Scheduled execution")
            .Build();

        // Assert
        definition.Trigger.Should().NotBeNull();
        definition.Trigger!.Type.Should().Be(Shared.Constants.TriggerType.Scheduled);
        definition.Trigger.CronExpression.Should().Be("0 */5 * * * *");
    }

    [Fact]
    public void Build_WithOutputMappings_ShouldConfigureMappings()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("mapping-workflow")
            .AddActivity("fetch", "http", a => a
                .WithProperty("url", "https://api.example.com/users")
                .WithInputMapping("userId", "input.userId")
                .WithOutputMapping("userData", "body"))
            .Build();

        // Assert
        var activity = definition.Activities[0];
        activity.InputMappings.Should().ContainKey("userId");
        activity.OutputMappings.Should().ContainKey("userData");
    }

    [Fact]
    public void Build_WithTimeout_ShouldSetTimeouts()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("timeout-workflow")
            .WithTimeout(TimeSpan.FromMinutes(30))
            .AddActivity("slow", "http", a => a
                .WithProperty("url", "https://slow-api.example.com")
                .WithTimeout(TimeSpan.FromMinutes(5)))
            .Build();

        // Assert
        definition.Timeout.Should().Be(TimeSpan.FromMinutes(30));
        definition.Activities[0].Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Build_WithInvalidStartActivity_ShouldThrow()
    {
        // Arrange
        var builder = WorkflowBuilder.Create("invalid")
            .AddLog("step1", "message")
            .StartsWith("nonexistent");

        // Act & Assert
        builder.Invoking(b => b.Build())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Start activity*not found*");
    }

    [Fact]
    public void Build_WithInvalidTransition_ShouldThrow()
    {
        // Arrange
        var builder = WorkflowBuilder.Create("invalid")
            .AddLog("step1", "message")
            .AddTransition("step1", "nonexistent");

        // Act & Assert
        builder.Invoking(b => b.Build())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Transition*not found*");
    }

    [Fact]
    public void Build_ComplexWorkflow_ShouldBuildSuccessfully()
    {
        // This test creates a realistic order processing workflow
        var definition = WorkflowBuilder.Create("order-processing")
            .WithDescription("Process incoming customer orders")
            .WithVersion(1)
            .WithTags("orders", "e-commerce")
            .WithTimeout(TimeSpan.FromHours(2))
            .WithInput(s => s
                .AddString("orderId", required: true)
                .AddNumber("amount", required: true)
                .AddString("customerId", required: true))
            .WithOutput(s => s
                .AddProperty("status", "string")
                .AddProperty("trackingNumber", "string"))
            .WithDefaultRetryPolicy(p => p.MaxAttempts(3).BackoffMultiplier(2))
            
            // Validate order
            .AddLog("validate", "Validating order ${input.orderId}")
            .Then("check-inventory")
            
            // Check inventory
            .AddHttp("check-inventory", "https://inventory.example.com/check", "POST", a => a
                .WithInputMapping("items", "input.items")
                .WithOutputMapping("available", "body.available"))
            
            // Branch based on availability
            .AddCondition("availability-check", c => c
                .When("state.available == true", "in-stock", "process-order")
                .Default("handle-backorder"))
            .AddTransition("check-inventory", "availability-check")
            
            // Process order (in stock)
            .AddHttp("process-order", "https://orders.example.com/process", "POST")
            .Then("send-confirmation")
            
            // Handle backorder
            .AddLog("handle-backorder", "Order ${input.orderId} backordered")
            .Then("send-backorder-notification")
            
            // Send confirmations
            .AddHttp("send-confirmation", "https://notifications.example.com/send", "POST")
            .Then("complete")
            
            .AddHttp("send-backorder-notification", "https://notifications.example.com/send", "POST")
            .Then("complete")
            
            // Complete
            .AddSetState("complete", new Dictionary<string, object?>
            {
                ["status"] = "completed",
                ["completedAt"] = "${now()}"
            })
            
            .Build();

        // Assert
        definition.Name.Should().Be("order-processing");
        definition.Activities.Should().HaveCount(8);
        definition.Transitions.Should().HaveCountGreaterThan(5);
        definition.InputSchema.Should().NotBeNull();
        definition.OutputSchema.Should().NotBeNull();
    }
}
