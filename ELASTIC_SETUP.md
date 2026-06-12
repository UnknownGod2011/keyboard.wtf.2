# Elastic Setup

## Required Server-Side Variables

```dotenv
ELASTICSEARCH_URL=https://your-elasticsearch-endpoint:443
ELASTICSEARCH_API_KEY=replace-with-elasticsearch-api-key
KIBANA_URL=https://your-kibana-endpoint
ELASTIC_AGENT_BUILDER_MCP_URL=https://your-kibana-endpoint/api/agent_builder/mcp
ELASTIC_MCP_API_KEY=replace-with-elastic-mcp-api-key
```

Never put these values in browser JavaScript.

## Indices

The service creates four indices when it starts:

- `keyboard_wtf_memories`
- `keyboard_wtf_actions`
- `keyboard_wtf_chats`
- `keyboard_wtf_failures`

Mappings use keywords for identity/status fields, dates for timestamps, and text for searchable content. Vector search is intentionally deferred until an embedding pipeline is working end to end.

## Memory Schema

```json
{
  "id": "uuid",
  "user_id": "required",
  "device_id": "required",
  "type": "semantic_memory",
  "title": "string",
  "content": "string",
  "summary": "string",
  "source_app": "string",
  "source_window": "string",
  "url": "string",
  "tags": ["keyword"],
  "importance": 5,
  "privacy_level": "normal",
  "action_status": "completed",
  "created_at": "date",
  "updated_at": "date",
  "last_accessed_at": "date",
  "metadata": {}
}
```

Supported types: semantic, episodic, action, document, chat, failure, preference, and link memory.

## Isolation Rules

- All save operations require a non-empty user context.
- Search/count/list always include a `term` filter for `user_id`.
- Delete fetches the document and verifies `user_id` before deletion.
- Clear uses `delete_by_query` with a `user_id` term.
- Action and failure lookups use the same scoped search helper.
- Sensitive memory is excluded by default.
- No global memory endpoint exists.

## Search

The working search path uses a scoped `multi_match` query over:

- title, boosted;
- summary, boosted;
- content;
- tags;
- URL.

It supports type, tag, date range, result limit, privacy, and relevance/date sorting. This is hybrid-ready, but no vector field is claimed in the current build.

## API Smoke Test

After the dashboard starts:

```powershell
Invoke-RestMethod http://localhost:8080/api/status
Invoke-RestMethod -Method Post http://localhost:8080/api/elastic/test
```

Use the dashboard to create, search, and delete a memory. The active `user_id` is displayed at all times.
