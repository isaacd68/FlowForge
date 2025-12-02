# FlowForge 

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)
[![Build](https://img.shields.io/badge/build-passing-brightgreen?style=flat-square)]()

**FlowForge** is a modern, distributed workflow engine built with C# and .NET 8. It provides a robust foundation for orchestrating complex business processes, data pipelines, and automated tasks with built-in support for retries, scheduling, parallel execution, and real-time monitoring.

## Features

- **Fluent Workflow Builder** - Define workflows with a clean, readable API
- **Built-in Activities** - HTTP requests, delays, conditions, loops, transforms, and more
- **Smart Retries** - Configurable exponential backoff with circuit breaker patterns
- **Cron Scheduling** - Schedule workflows with standard cron expressions
- **Branching & Conditions** - Complex decision trees with JavaScript expressions
- **Suspend & Resume** - Wait for external signals or human approval
- **Child Workflows** - Compose workflows from reusable sub-workflows
- **Real-time Monitoring** - SignalR hub for live workflow status updates
- **PostgreSQL Persistence** - Durable storage with JSONB for flexibility
- **Redis Caching** - Distributed locking and message queue
- **Docker Ready** - Production-ready container configuration
- **Fully Tested** - Comprehensive test suite with xUnit

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              FlowForge                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐              │
│  │   REST API   │    │    Worker    │    │  Dashboard   │              │
│  │  (ASP.NET)   │    │  (Background)│    │   (Blazor)   │              │
│  └──────┬───────┘    └──────┬───────┘    └──────────────┘              │
│         │                   │                                           │
│         └─────────┬─────────┘                                           │
│                   │                                                      │
│         ┌─────────▼─────────┐                                           │
│         │   FlowForge.Core  │                                           │
│         │  ┌─────────────┐  │                                           │
│         │  │   Engine    │  │                                           │
│         │  │  Activities │  │                                           │
│         │  │ Expressions │  │                                           │
│         │  │  Scheduler  │  │                                           │
│         │  └─────────────┘  │                                           │
│         └─────────┬─────────┘                                           │
│                   │                                                      │
│    ┌──────────────┼──────────────┐                                      │
│    │              │              │                                       │
│ ┌──▼───┐    ┌─────▼─────┐   ┌───▼────┐                                 │
│ │Redis │    │PostgreSQL │   │ SignalR│                                 │
│ │Cache │    │  Storage  │   │  Hub   │                                 │
│ │Queue │    │           │   │        │                                 │
│ └──────┘    └───────────┘   └────────┘                                 │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/get-started) (for PostgreSQL & Redis)

### Using Docker Compose (Recommended)

```bash
# Clone the repository
git clone https://github.com/yourusername/FlowForge.git
cd FlowForge

# Start all services
cd docker
docker-compose up -d

# The API will be available at http://localhost:5000
# Swagger UI at http://localhost:5000/swagger
```

### Manual Setup

```bash
# Start PostgreSQL and Redis
docker run -d --name flowforge-postgres -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=flowforge postgres:16-alpine

docker run -d --name flowforge-redis -p 6379:6379 redis:7-alpine

# Run the API
cd src/FlowForge.Api
dotnet run

# Run a worker (in another terminal)
cd src/FlowForge.Worker
dotnet run
```

## Usage

### Define a Workflow

```csharp
using FlowForge.Core.Workflows;

var workflow = WorkflowBuilder.Create("order-processing")
    .WithDescription("Process customer orders")
    .WithInput(schema => schema
        .AddString("orderId", required: true)
        .AddNumber("amount", required: true))
    
    // Step 1: Log the order
    .AddLog("log-start", "Processing order ${input.orderId}")
    .Then("validate")
    
    // Step 2: Validate
    .AddActivity("validate", "transform", a => a
        .WithProperty("mappings", new Dictionary<string, string>
        {
            ["isValid"] = "input.amount > 0"
        })
        .WithOutputMapping("valid", "isValid"))
    .Then("check-valid")
    
    // Step 3: Branch based on validation
    .AddCondition("check-valid", c => c
        .When("state.valid == true", "valid", "process-payment")
        .Default("reject-order"))
    
    // Step 4: Process payment
    .AddHttp("process-payment", "https://api.example.com/charge", "POST")
    .Then("complete")
    
    // Step 5: Complete
    .AddSetState("complete", new Dictionary<string, object?>
    {
        ["status"] = "completed"
    })
    
    // Error handler
    .AddLog("reject-order", "Order ${input.orderId} rejected")
    
    .Build();
```

### Start a Workflow via API

```bash
curl -X POST http://localhost:5000/api/workflows \
  -H "Content-Type: application/json" \
  -d '{
    "workflowName": "order-processing",
    "input": {
      "orderId": "ORD-12345",
      "amount": 99.99
    }
  }'
```

### Monitor in Real-time

```javascript
// Connect to SignalR hub
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/workflow")
    .build();

connection.on("WorkflowUpdated", (event) => {
    console.log(`Workflow ${event.instanceId}: ${event.status}`);
});

await connection.start();
await connection.invoke("SubscribeToWorkflow", instanceId);
```

## Built-in Activities

| Activity | Description |
|----------|-------------|
| `log` | Write a message to the log |
| `delay` | Wait for a specified duration |
| `http` | Make HTTP requests |
| `transform` | Transform data using expressions |
| `condition` | Branch based on conditions |
| `forEach` | Iterate over collections |
| `setState` | Set workflow state values |
| `waitForSignal` | Suspend and wait for external signal |
| `invokeWorkflow` | Start a child workflow |
| `parallel` | Execute activities in parallel |

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Database=flowforge;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "FlowForge": {
    "EnableScheduler": true,
    "DetailedLogging": true
  }
}
```

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ConnectionStrings__Postgres` | PostgreSQL connection string | - |
| `ConnectionStrings__Redis` | Redis connection string | - |
| `FlowForge__EnableScheduler` | Enable cron scheduler | `true` |
| `Worker__MaxConcurrency` | Max concurrent workflows per worker | `10` |

## Project Structure

```
FlowForge/
├── src/
│   ├── FlowForge.Core/           # Core workflow engine
│   │   ├── Activities/           # Built-in activity types
│   │   ├── Expressions/          # JavaScript expression evaluation
│   │   ├── Scheduling/           # Cron-based scheduling
│   │   └── Workflows/            # Engine and builder
│   ├── FlowForge.Api/            # REST API
│   │   ├── Controllers/          # API endpoints
│   │   ├── Hubs/                 # SignalR hubs
│   │   └── Middleware/           # Error handling, logging
│   ├── FlowForge.Worker/         # Background job processor
│   ├── FlowForge.Shared/         # Shared models and contracts
│   ├── FlowForge.Persistence.Postgres/  # PostgreSQL implementation
│   └── FlowForge.Persistence.Redis/     # Redis caching/locking
├── tests/
│   └── FlowForge.Core.Tests/     # Unit tests
├── samples/
│   └── OrderProcessing/          # Sample workflow
├── docker/
│   ├── docker-compose.yml        # Development environment
│   ├── Dockerfile.api            # API container
│   └── Dockerfile.worker         # Worker container
└── docs/                         # Documentation
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/FlowForge.Core.Tests
```

## API Reference

### Workflows

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/workflows` | Start a new workflow |
| `GET` | `/api/workflows` | List workflow instances |
| `GET` | `/api/workflows/{id}` | Get workflow instance |
| `POST` | `/api/workflows/{id}/cancel` | Cancel a workflow |
| `POST` | `/api/workflows/{id}/signal/{name}` | Send a signal |
| `DELETE` | `/api/workflows/{id}` | Delete workflow instance |

### Definitions

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/definitions` | List workflow definitions |
| `GET` | `/api/definitions/{name}` | Get workflow definition |
| `POST` | `/api/definitions` | Create/update definition |
| `POST` | `/api/definitions/{name}/versions/{v}/activate` | Activate version |
| `DELETE` | `/api/definitions/{name}/versions/{v}` | Delete version |

## Roadmap

- [ ] Blazor Dashboard UI
- [ ] GraphQL API
- [ ] Workflow versioning and migration
- [ ] Dead letter queue for failed workflows
- [ ] Workflow templates and marketplace
- [ ] Kubernetes Helm charts
- [ ] OpenTelemetry integration
- [ ] YAML workflow definitions


## Acknowledgments

- [Jint](https://github.com/sebastienros/jint) - JavaScript interpreter for .NET
- [Cronos](https://github.com/HangfireIO/Cronos) - Cron expression parser
- [Polly](https://github.com/App-vNext/Polly) - Resilience and transient-fault-handling

