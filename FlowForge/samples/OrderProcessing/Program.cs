using FlowForge.Core.Activities;
using FlowForge.Core.Workflows;

namespace OrderProcessing;

/// <summary>
/// Sample demonstrating an order processing workflow built with FlowForge.
/// </summary>
public static class Program
{
    public static void Main()
    {
        // Build the order processing workflow
        var workflow = BuildOrderProcessingWorkflow();
        
        Console.WriteLine("=== FlowForge Order Processing Workflow ===\n");
        Console.WriteLine($"Workflow: {workflow.Name} v{workflow.Version}");
        Console.WriteLine($"Description: {workflow.Description}");
        Console.WriteLine($"Activities: {workflow.Activities.Count}");
        Console.WriteLine($"Transitions: {workflow.Transitions.Count}");
        Console.WriteLine();
        
        // Print the workflow structure
        PrintWorkflowStructure(workflow);
        
        // Print input/output schemas
        PrintSchemas(workflow);
    }

    /// <summary>
    /// Builds a comprehensive order processing workflow.
    /// </summary>
    private static Shared.Models.WorkflowDefinition BuildOrderProcessingWorkflow()
    {
        return WorkflowBuilder.Create("order-processing")
            .WithDescription("End-to-end order processing workflow with inventory check, payment, and fulfillment")
            .WithVersion(1)
            .WithTags("orders", "e-commerce", "production")
            .WithTimeout(TimeSpan.FromHours(4))
            
            // Input validation schema
            .WithInput(schema => schema
                .AddString("orderId", required: true, description: "Unique order identifier")
                .AddString("customerId", required: true, description: "Customer ID")
                .AddNumber("totalAmount", required: true, description: "Order total in USD")
                .AddArray("items", required: true, description: "Array of order items")
                .AddString("shippingAddress", required: true, description: "Shipping address")
                .AddString("paymentMethod", required: true, description: "Payment method (credit_card, paypal, etc.)"))
            
            // Output schema
            .WithOutput(schema => schema
                .AddProperty("status", "string", "Final order status")
                .AddProperty("trackingNumber", "string", "Shipping tracking number")
                .AddProperty("estimatedDelivery", "string", "Estimated delivery date")
                .AddProperty("confirmationNumber", "string", "Order confirmation number"))
            
            // Default retry policy
            .WithDefaultRetryPolicy(policy => policy
                .MaxAttempts(3)
                .InitialDelay(TimeSpan.FromSeconds(2))
                .MaxDelay(TimeSpan.FromMinutes(5))
                .BackoffMultiplier(2.0))
            
            // === STEP 1: Validate Order ===
            .AddLog("log-start", "Processing order ${input.orderId} for customer ${input.customerId}")
            .Then("validate-order")
            
            .AddActivity("validate-order", "transform", a => a
                .WithName("Validate Order Data")
                .WithProperty("mappings", new Dictionary<string, string>
                {
                    ["isValid"] = "input.totalAmount > 0",
                    ["itemCount"] = "length(input.items)",
                    ["validationTimestamp"] = "now()"
                })
                .WithOutputMapping("orderValid", "isValid")
                .WithOutputMapping("itemCount", "itemCount"))
            .Then("check-validation")
            
            // === STEP 2: Check Validation Result ===
            .AddCondition("check-validation", c => c
                .When("state.orderValid == true", "valid", "check-inventory")
                .Default("reject-order"))
            
            // === STEP 3: Check Inventory ===
            .AddHttp("check-inventory", "https://inventory.api.example.com/check", "POST", a => a
                .WithName("Check Inventory Availability")
                .WithProperty("body", new { items = "${input.items}" })
                .WithProperty("headers", new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer ${env.INVENTORY_API_KEY}",
                    ["Content-Type"] = "application/json"
                })
                .WithTimeout(TimeSpan.FromSeconds(30))
                .WithOutputMapping("inventoryStatus", "body"))
            .Then("evaluate-inventory")
            
            .AddCondition("evaluate-inventory", c => c
                .When("state.inventoryStatus.available == true", "in-stock", "process-payment")
                .When("state.inventoryStatus.partialAvailable == true", "partial", "handle-partial")
                .Default("handle-out-of-stock"))
            
            // === STEP 4: Process Payment ===
            .AddHttp("process-payment", "https://payments.api.example.com/charge", "POST", a => a
                .WithName("Process Payment")
                .WithProperty("body", new
                {
                    orderId = "${input.orderId}",
                    amount = "${input.totalAmount}",
                    method = "${input.paymentMethod}",
                    customerId = "${input.customerId}"
                })
                .WithRetryPolicy(p => p
                    .MaxAttempts(2)
                    .InitialDelay(TimeSpan.FromSeconds(5)))
                .WithOutputMapping("paymentResult", "body"))
            .Then("check-payment")
            
            .AddCondition("check-payment", c => c
                .When("state.paymentResult.success == true", "paid", "reserve-inventory")
                .Default("payment-failed"))
            
            // === STEP 5: Reserve Inventory ===
            .AddHttp("reserve-inventory", "https://inventory.api.example.com/reserve", "POST", a => a
                .WithName("Reserve Inventory")
                .WithProperty("body", new { orderId = "${input.orderId}", items = "${input.items}" })
                .WithOutputMapping("reservationId", "body.reservationId"))
            .Then("create-shipment")
            
            // === STEP 6: Create Shipment ===
            .AddHttp("create-shipment", "https://shipping.api.example.com/shipments", "POST", a => a
                .WithName("Create Shipment")
                .WithProperty("body", new
                {
                    orderId = "${input.orderId}",
                    address = "${input.shippingAddress}",
                    items = "${input.items}"
                })
                .WithOutputMapping("trackingNumber", "body.trackingNumber")
                .WithOutputMapping("estimatedDelivery", "body.estimatedDelivery"))
            .Then("send-confirmation")
            
            // === STEP 7: Send Confirmation ===
            .AddHttp("send-confirmation", "https://notifications.api.example.com/send", "POST", a => a
                .WithName("Send Order Confirmation")
                .WithProperty("body", new
                {
                    type = "order_confirmation",
                    customerId = "${input.customerId}",
                    orderId = "${input.orderId}",
                    trackingNumber = "${state.trackingNumber}",
                    estimatedDelivery = "${state.estimatedDelivery}"
                }))
            .Then("complete-success")
            
            // === SUCCESS: Complete Order ===
            .AddSetState("complete-success", new Dictionary<string, object?>
            {
                ["status"] = "completed",
                ["confirmationNumber"] = "${uuid()}"
            })
            .Then("log-success")
            
            .AddLog("log-success", "Order ${input.orderId} completed successfully. Tracking: ${state.trackingNumber}")
            
            // === ERROR HANDLERS ===
            
            // Reject invalid order
            .AddLog("reject-order", "Order ${input.orderId} rejected: invalid data")
            .Then("notify-rejection")
            
            .AddHttp("notify-rejection", "https://notifications.api.example.com/send", "POST", a => a
                .WithProperty("body", new { type = "order_rejected", orderId = "${input.orderId}" }))
            .Then("complete-rejected")
            
            .AddSetState("complete-rejected", new Dictionary<string, object?>
            {
                ["status"] = "rejected",
                ["reason"] = "Invalid order data"
            })
            
            // Handle partial availability
            .AddLog("handle-partial", "Order ${input.orderId} has partial inventory availability")
            .Then("wait-customer-decision")
            
            .AddWaitForSignal("wait-customer-decision", "customer_decision", timeoutSeconds: 86400)
            .ThenIf("state.signal_decision == \"proceed\"", "process-payment")
            .ThenDefault("cancel-order")
            
            // Handle out of stock
            .AddLog("handle-out-of-stock", "Order ${input.orderId} cannot be fulfilled - out of stock")
            .Then("notify-out-of-stock")
            
            .AddHttp("notify-out-of-stock", "https://notifications.api.example.com/send", "POST", a => a
                .WithProperty("body", new { type = "out_of_stock", orderId = "${input.orderId}" }))
            .Then("complete-out-of-stock")
            
            .AddSetState("complete-out-of-stock", new Dictionary<string, object?>
            {
                ["status"] = "cancelled",
                ["reason"] = "Items out of stock"
            })
            
            // Handle payment failure
            .AddLog("payment-failed", "Payment failed for order ${input.orderId}")
            .Then("notify-payment-failed")
            
            .AddHttp("notify-payment-failed", "https://notifications.api.example.com/send", "POST", a => a
                .WithProperty("body", new { type = "payment_failed", orderId = "${input.orderId}" }))
            .Then("complete-payment-failed")
            
            .AddSetState("complete-payment-failed", new Dictionary<string, object?>
            {
                ["status"] = "failed",
                ["reason"] = "Payment declined"
            })
            
            // Cancel order
            .AddLog("cancel-order", "Order ${input.orderId} cancelled by customer")
            .Then("refund-if-needed")
            
            .AddCondition("refund-if-needed", c => c
                .When("state.paymentResult != null", "has-payment", "process-refund")
                .Default("complete-cancelled"))
            
            .AddHttp("process-refund", "https://payments.api.example.com/refund", "POST", a => a
                .WithProperty("body", new { orderId = "${input.orderId}" }))
            .Then("complete-cancelled")
            
            .AddSetState("complete-cancelled", new Dictionary<string, object?>
            {
                ["status"] = "cancelled",
                ["reason"] = "Cancelled by customer"
            })
            
            .Build();
    }

    private static void PrintWorkflowStructure(Shared.Models.WorkflowDefinition workflow)
    {
        Console.WriteLine("=== Workflow Activities ===\n");
        
        foreach (var activity in workflow.Activities)
        {
            Console.WriteLine($"  [{activity.Id}]");
            Console.WriteLine($"    Type: {activity.Type}");
            if (!string.IsNullOrEmpty(activity.Name))
                Console.WriteLine($"    Name: {activity.Name}");
            if (activity.Timeout.HasValue)
                Console.WriteLine($"    Timeout: {activity.Timeout.Value.TotalSeconds}s");
            Console.WriteLine();
        }
        
        Console.WriteLine("=== Transitions ===\n");
        
        foreach (var transition in workflow.Transitions)
        {
            var condition = string.IsNullOrEmpty(transition.Condition) 
                ? (transition.IsDefault ? "(default)" : "(always)") 
                : $"when: {transition.Condition}";
            Console.WriteLine($"  {transition.From} -> {transition.To} {condition}");
        }
        
        Console.WriteLine();
    }

    private static void PrintSchemas(Shared.Models.WorkflowDefinition workflow)
    {
        if (workflow.InputSchema is not null)
        {
            Console.WriteLine("=== Input Schema ===\n");
            foreach (var (name, prop) in workflow.InputSchema.Properties)
            {
                var required = workflow.InputSchema.Required.Contains(name) ? "*" : "";
                Console.WriteLine($"  {name}{required}: {prop.Type}");
                if (!string.IsNullOrEmpty(prop.Description))
                    Console.WriteLine($"    {prop.Description}");
            }
            Console.WriteLine();
        }
        
        if (workflow.OutputSchema is not null)
        {
            Console.WriteLine("=== Output Schema ===\n");
            foreach (var (name, prop) in workflow.OutputSchema.Properties)
            {
                Console.WriteLine($"  {name}: {prop.Type}");
                if (!string.IsNullOrEmpty(prop.Description))
                    Console.WriteLine($"    {prop.Description}");
            }
        }
    }
}
