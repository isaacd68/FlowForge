using FlowForge.Core.Workflows;

namespace DataPipeline;

/// <summary>
/// Sample demonstrating an ETL data pipeline workflow built with FlowForge.
/// </summary>
public static class Program
{
    public static void Main()
    {
        // Build the data pipeline workflow
        var workflow = BuildDataPipelineWorkflow();
        
        Console.WriteLine("=== FlowForge Data Pipeline Workflow ===\n");
        Console.WriteLine($"Workflow: {workflow.Name} v{workflow.Version}");
        Console.WriteLine($"Description: {workflow.Description}");
        Console.WriteLine($"Activities: {workflow.Activities.Count}");
        Console.WriteLine($"Scheduled: {workflow.Trigger?.CronExpression ?? "Manual"}");
        Console.WriteLine();
        
        PrintWorkflowSteps(workflow);
    }

    /// <summary>
    /// Builds an ETL data pipeline workflow.
    /// </summary>
    private static Shared.Models.WorkflowDefinition BuildDataPipelineWorkflow()
    {
        return WorkflowBuilder.Create("daily-data-sync")
            .WithDescription("Daily ETL pipeline to sync data from multiple sources to data warehouse")
            .WithVersion(1)
            .WithTags("etl", "data-pipeline", "scheduled")
            .WithScheduledTrigger("0 0 2 * * *") // Run daily at 2 AM
            .WithTimeout(TimeSpan.FromHours(6))
            
            .WithInput(schema => schema
                .AddString("runDate", description: "Date to process (defaults to yesterday)")
                .AddBoolean("fullRefresh", description: "Whether to do full refresh instead of incremental"))
            
            .WithOutput(schema => schema
                .AddProperty("recordsProcessed", "number")
                .AddProperty("duration", "string")
                .AddProperty("status", "string"))
            
            .WithDefaultRetryPolicy(p => p
                .MaxAttempts(3)
                .InitialDelay(TimeSpan.FromMinutes(1))
                .BackoffMultiplier(2))
            
            // === INITIALIZATION ===
            .AddLog("start", "Starting data pipeline for ${input.runDate ?? 'yesterday'}")
            .Then("init-state")
            
            .AddSetState("init-state", new Dictionary<string, object?>
            {
                ["startTime"] = "${now()}",
                ["recordsProcessed"] = 0,
                ["errors"] = new List<string>()
            })
            .Then("extract-source-a")
            
            // === EXTRACT PHASE ===
            
            // Source A: CRM Database
            .AddHttp("extract-source-a", "https://crm-api.example.com/export", "POST", a => a
                .WithName("Extract CRM Data")
                .WithProperty("body", new { date = "${input.runDate}", format = "json" })
                .WithProperty("timeoutSeconds", 300)
                .WithOutputMapping("crmData", "body.records")
                .WithOutputMapping("crmCount", "body.count"))
            .Then("extract-source-b")
            
            // Source B: ERP System
            .AddHttp("extract-source-b", "https://erp-api.example.com/transactions", "GET", a => a
                .WithName("Extract ERP Transactions")
                .WithProperty("headers", new Dictionary<string, string>
                {
                    ["X-Date-Range"] = "${input.runDate}"
                })
                .WithOutputMapping("erpData", "body.transactions")
                .WithOutputMapping("erpCount", "body.total"))
            .Then("extract-source-c")
            
            // Source C: Web Analytics
            .AddHttp("extract-source-c", "https://analytics.example.com/api/events", "POST", a => a
                .WithName("Extract Analytics Events")
                .WithProperty("body", new { date = "${input.runDate}" })
                .WithOutputMapping("analyticsData", "body.events")
                .WithOutputMapping("analyticsCount", "body.count"))
            .Then("log-extract-complete")
            
            .AddLog("log-extract-complete", 
                "Extraction complete: CRM=${state.crmCount}, ERP=${state.erpCount}, Analytics=${state.analyticsCount}")
            .Then("transform-crm")
            
            // === TRANSFORM PHASE ===
            
            // Transform CRM data
            .AddActivity("transform-crm", "transform", a => a
                .WithName("Transform CRM Data")
                .WithProperty("mappings", new Dictionary<string, string>
                {
                    ["transformedCrm"] = @"state.crmData.map(r => ({
                        customerId: r.id,
                        name: r.firstName + ' ' + r.lastName,
                        email: lower(r.email),
                        createdAt: r.created_at,
                        source: 'crm'
                    }))"
                }))
            .Then("transform-erp")
            
            // Transform ERP data
            .AddActivity("transform-erp", "transform", a => a
                .WithName("Transform ERP Transactions")
                .WithProperty("mappings", new Dictionary<string, string>
                {
                    ["transformedErp"] = @"state.erpData.map(t => ({
                        transactionId: t.txn_id,
                        amount: round(t.amount, 2),
                        currency: upper(t.currency),
                        timestamp: t.created,
                        source: 'erp'
                    }))"
                }))
            .Then("transform-analytics")
            
            // Transform Analytics data
            .AddActivity("transform-analytics", "transform", a => a
                .WithName("Transform Analytics Events")
                .WithProperty("mappings", new Dictionary<string, string>
                {
                    ["transformedAnalytics"] = @"state.analyticsData.filter(e => e.event_type !== 'internal').map(e => ({
                        eventId: e.id,
                        eventType: e.event_type,
                        userId: e.user_id,
                        timestamp: e.ts,
                        source: 'analytics'
                    }))"
                }))
            .Then("validate-data")
            
            // === VALIDATION ===
            .AddActivity("validate-data", "transform", a => a
                .WithName("Validate Transformed Data")
                .WithProperty("mappings", new Dictionary<string, string>
                {
                    ["crmValid"] = "length(state.transformedCrm) > 0",
                    ["erpValid"] = "length(state.transformedErp) > 0",
                    ["analyticsValid"] = "length(state.transformedAnalytics) > 0",
                    ["allValid"] = "state.crmValid && state.erpValid && state.analyticsValid"
                }))
            .Then("check-validation")
            
            .AddCondition("check-validation", c => c
                .When("state.allValid == true", "valid", "load-customers")
                .Default("handle-validation-error"))
            
            // === LOAD PHASE ===
            
            // Load customers to warehouse
            .AddHttp("load-customers", "https://warehouse.example.com/api/customers/bulk", "POST", a => a
                .WithName("Load Customers to Warehouse")
                .WithProperty("body", new { records = "${state.transformedCrm}" })
                .WithOutputMapping("customersLoaded", "body.inserted"))
            .Then("load-transactions")
            
            // Load transactions to warehouse
            .AddHttp("load-transactions", "https://warehouse.example.com/api/transactions/bulk", "POST", a => a
                .WithName("Load Transactions to Warehouse")
                .WithProperty("body", new { records = "${state.transformedErp}" })
                .WithOutputMapping("transactionsLoaded", "body.inserted"))
            .Then("load-events")
            
            // Load events to warehouse
            .AddHttp("load-events", "https://warehouse.example.com/api/events/bulk", "POST", a => a
                .WithName("Load Events to Warehouse")
                .WithProperty("body", new { records = "${state.transformedAnalytics}" })
                .WithOutputMapping("eventsLoaded", "body.inserted"))
            .Then("calculate-totals")
            
            // === FINALIZATION ===
            .AddActivity("calculate-totals", "transform", a => a
                .WithProperty("mappings", new Dictionary<string, string>
                {
                    ["recordsProcessed"] = "state.customersLoaded + state.transactionsLoaded + state.eventsLoaded",
                    ["endTime"] = "now()"
                }))
            .Then("update-metadata")
            
            .AddHttp("update-metadata", "https://warehouse.example.com/api/sync/complete", "POST", a => a
                .WithName("Update Sync Metadata")
                .WithProperty("body", new
                {
                    runDate = "${input.runDate}",
                    recordsProcessed = "${state.recordsProcessed}",
                    startTime = "${state.startTime}",
                    endTime = "${state.endTime}"
                }))
            .Then("notify-success")
            
            .AddHttp("notify-success", "https://slack.example.com/webhook", "POST", a => a
                .WithName("Send Success Notification")
                .WithProperty("body", new
                {
                    text = "✅ Data pipeline completed successfully",
                    blocks = new[]
                    {
                        new { type = "section", text = new { type = "mrkdwn", 
                            text = "Records processed: ${state.recordsProcessed}" } }
                    }
                }))
            .Then("complete-success")
            
            .AddSetState("complete-success", new Dictionary<string, object?>
            {
                ["status"] = "completed"
            })
            
            // === ERROR HANDLERS ===
            
            .AddLog("handle-validation-error", "Data validation failed - some sources returned empty data")
            .Then("notify-failure")
            
            .AddHttp("notify-failure", "https://slack.example.com/webhook", "POST", a => a
                .WithName("Send Failure Notification")
                .WithProperty("body", new
                {
                    text = "❌ Data pipeline failed",
                    blocks = new[]
                    {
                        new { type = "section", text = new { type = "mrkdwn",
                            text = "Please check the logs for details" } }
                    }
                }))
            .Then("complete-failed")
            
            .AddSetState("complete-failed", new Dictionary<string, object?>
            {
                ["status"] = "failed"
            })
            
            .Build();
    }

    private static void PrintWorkflowSteps(Shared.Models.WorkflowDefinition workflow)
    {
        Console.WriteLine("=== Pipeline Stages ===\n");
        
        var stages = new[]
        {
            ("📥 EXTRACT", new[] { "extract-source-a", "extract-source-b", "extract-source-c" }),
            ("🔄 TRANSFORM", new[] { "transform-crm", "transform-erp", "transform-analytics", "validate-data" }),
            ("📤 LOAD", new[] { "load-customers", "load-transactions", "load-events" }),
            ("✅ FINALIZE", new[] { "calculate-totals", "update-metadata", "notify-success" })
        };

        foreach (var (stageName, activityIds) in stages)
        {
            Console.WriteLine($"{stageName}");
            foreach (var id in activityIds)
            {
                var activity = workflow.Activities.FirstOrDefault(a => a.Id == id);
                if (activity != null)
                {
                    Console.WriteLine($"  → {activity.Name ?? activity.Id} ({activity.Type})");
                }
            }
            Console.WriteLine();
        }
    }
}
