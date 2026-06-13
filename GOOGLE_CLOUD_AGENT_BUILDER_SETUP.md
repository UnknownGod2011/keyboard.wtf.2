# Google Cloud Agent Platform and Elastic MCP Setup

## Google Configuration

- Project: `keyboard-wtf-agent`
- Location: `asia-south1`
- Vertex AI runtime location: `global`
- Agent Studio app: `keyboard.wtf Agent`
- Model: `gemini-3.1-pro-preview`

Enable the required APIs in the Google Cloud console for the selected Agent Platform workflow.

Cloud Run uses its runtime service account with `roles/aiplatform.user`. Set
`GOOGLE_GENAI_USE_VERTEXAI=true` and keep Gemini credentials server-side.

## Runtime ADK Integration

The Node backend imports `MCPToolset` from `@google/adk` and creates:

```ts
new MCPToolset({
  type: "StreamableHTTPConnectionParams",
  url: process.env.ELASTIC_AGENT_BUILDER_MCP_URL,
  transportOptions: {
    requestInit: {
      headers: {
        Authorization: `ApiKey ${process.env.ELASTIC_MCP_API_KEY}`
      }
    }
  }
});
```

The API key stays server-side. The toolset is closed after discovery.

## Verification

1. Configure the MCP URL and API key.
2. Start the dashboard.
3. Request `GET /api/compliance`.
4. Inspect `elasticMcp.connected` and `elasticMcp.toolNames`.

This endpoint performs live discovery. It does not return the API key. The submitted Cloud Run service currently discovers Elastic Agent Builder MCP tools successfully.

## Honest Fallback

If the Elastic MCP endpoint or SDK cannot connect during judging, the app remains functional through direct Elasticsearch APIs. The dashboard reports MCP as disconnected and does not mark it as successful.

The direct Elasticsearch path is responsible for production memory CRUD, retrieval, actions, and failures. MCP demonstrates partner tool interoperability through the official Google ADK path.
