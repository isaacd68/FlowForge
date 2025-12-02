-- FlowForge Database Initialization Script
-- This script is automatically run when the PostgreSQL container starts

-- Enable extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Workflow Definitions table
CREATE TABLE IF NOT EXISTS workflow_definitions (
    name VARCHAR(256) NOT NULL,
    version INTEGER NOT NULL,
    description VARCHAR(2000),
    start_activity_id VARCHAR(256) NOT NULL,
    activities JSONB,
    transitions JSONB,
    input_schema JSONB,
    output_schema JSONB,
    trigger JSONB,
    default_retry_policy JSONB,
    timeout INTERVAL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    tags JSONB NOT NULL DEFAULT '[]',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (name, version)
);

CREATE INDEX IF NOT EXISTS idx_workflow_definitions_name ON workflow_definitions(name);
CREATE INDEX IF NOT EXISTS idx_workflow_definitions_is_active ON workflow_definitions(is_active);
CREATE INDEX IF NOT EXISTS idx_workflow_definitions_created_at ON workflow_definitions(created_at);

-- Workflow Instances table
CREATE TABLE IF NOT EXISTS workflow_instances (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    workflow_name VARCHAR(256) NOT NULL,
    workflow_version INTEGER NOT NULL,
    status INTEGER NOT NULL DEFAULT 0,
    input JSONB,
    output JSONB,
    state JSONB,
    error JSONB,
    parent_instance_id UUID,
    correlation_id VARCHAR(256),
    current_activity_id VARCHAR(256),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    retry_count INTEGER NOT NULL DEFAULT 0,
    worker_id VARCHAR(256),
    tags JSONB NOT NULL DEFAULT '[]',
    metadata JSONB
);

CREATE INDEX IF NOT EXISTS idx_workflow_instances_workflow_name ON workflow_instances(workflow_name);
CREATE INDEX IF NOT EXISTS idx_workflow_instances_status ON workflow_instances(status);
CREATE INDEX IF NOT EXISTS idx_workflow_instances_correlation_id ON workflow_instances(correlation_id);
CREATE INDEX IF NOT EXISTS idx_workflow_instances_parent_instance_id ON workflow_instances(parent_instance_id);
CREATE INDEX IF NOT EXISTS idx_workflow_instances_created_at ON workflow_instances(created_at);
CREATE INDEX IF NOT EXISTS idx_workflow_instances_updated_at ON workflow_instances(updated_at);
CREATE INDEX IF NOT EXISTS idx_workflow_instances_status_updated ON workflow_instances(status, updated_at);

-- Activity Executions table
CREATE TABLE IF NOT EXISTS activity_executions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    workflow_instance_id UUID NOT NULL REFERENCES workflow_instances(id) ON DELETE CASCADE,
    activity_id VARCHAR(256) NOT NULL,
    activity_type VARCHAR(256) NOT NULL,
    status INTEGER NOT NULL DEFAULT 0,
    input JSONB,
    output JSONB,
    error JSONB,
    attempt INTEGER NOT NULL DEFAULT 1,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    duration_ms BIGINT,
    worker_id VARCHAR(256)
);

CREATE INDEX IF NOT EXISTS idx_activity_executions_workflow_instance ON activity_executions(workflow_instance_id);
CREATE INDEX IF NOT EXISTS idx_activity_executions_activity ON activity_executions(workflow_instance_id, activity_id);
CREATE INDEX IF NOT EXISTS idx_activity_executions_started_at ON activity_executions(started_at);

-- Function to update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Trigger to auto-update updated_at
DROP TRIGGER IF EXISTS update_workflow_definitions_updated_at ON workflow_definitions;
CREATE TRIGGER update_workflow_definitions_updated_at
    BEFORE UPDATE ON workflow_definitions
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

DROP TRIGGER IF EXISTS update_workflow_instances_updated_at ON workflow_instances;
CREATE TRIGGER update_workflow_instances_updated_at
    BEFORE UPDATE ON workflow_instances
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Insert sample workflow definition
INSERT INTO workflow_definitions (name, version, description, start_activity_id, activities, transitions, is_active, tags)
VALUES (
    'hello-world',
    1,
    'A simple hello world workflow for testing',
    'greet',
    '[
        {"id": "greet", "type": "log", "name": "Greet", "properties": {"message": "Hello, ${input.name}!", "level": "Information"}},
        {"id": "delay", "type": "delay", "name": "Wait", "properties": {"delayMs": 1000}},
        {"id": "farewell", "type": "log", "name": "Farewell", "properties": {"message": "Goodbye, ${input.name}!", "level": "Information"}}
    ]'::jsonb,
    '[
        {"from": "greet", "to": "delay"},
        {"from": "delay", "to": "farewell"}
    ]'::jsonb,
    true,
    '["sample", "hello-world"]'::jsonb
)
ON CONFLICT (name, version) DO NOTHING;

-- Grant permissions (if using a different user)
-- GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO flowforge_user;
-- GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO flowforge_user;

COMMENT ON TABLE workflow_definitions IS 'Stores workflow definition blueprints';
COMMENT ON TABLE workflow_instances IS 'Stores running and completed workflow instances';
COMMENT ON TABLE activity_executions IS 'Stores execution history of individual activities';
