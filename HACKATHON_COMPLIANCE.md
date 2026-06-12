# Hackathon Compliance

## Submission

- Project: keyboard.wtf.2
- Submission name: keyboard.wtf - Jarvis for your PC
- Target track: Elastic
- Public repository: `https://github.com/UnknownGod2011/keyboard.wtf.2`
- Hosted URL: add the Cloud Run URL after deployment
- License: MIT, visible at the repository root
- Demo plan: [DEMO_SCRIPT.md](DEMO_SCRIPT.md)

## Functional Agent

The agent does more than chat:

1. It retrieves a small set of relevant Elastic memories.
2. Gemini creates a structured intent and action plan.
3. The plan is checked against an allowlisted registry.
4. Risky actions require confirmation.
5. The paired local bridge executes the approved action.
6. The verified result or failure is written to Elastic.

The model cannot produce a shell command for execution.

## Gemini

Gemini is the only active reasoning provider in this hackathon build. It:

- interprets requests;
- receives only top relevant memory context;
- produces structured JSON plans;
- chooses only registered tools;
- explains failures without inventing success.

The Windows app also retains its existing Gemini Live voice conversation.

## Google Cloud Agent Platform and ADK

The project uses the official Google ADK TypeScript package. `elasticMcpClient.ts` creates an authenticated Streamable HTTP `MCPToolset` and discovers tools from Elastic Agent Builder MCP.

The Google Cloud project and Agent Studio app remain the orchestration/compliance setup. The judge-facing backend is prepared for Cloud Run in `asia-south1`.

## Elastic

Elastic is the central memory and audit layer, not a decorative status check.

- memories, links, preferences, and documents;
- chat summaries;
- completed and confirmation-required actions;
- failures;
- keyword and fuzzy multi-field retrieval;
- date, type, tag, privacy, and user filters;
- per-user memory count;
- dashboard search, delete, and clear.

## Elastic MCP

Runtime implementation:

```text
Google ADK MCPToolset
  -> Streamable HTTP
  -> {KIBANA_URL}/api/agent_builder/mcp
  -> Authorization: ApiKey ...
```

`GET /api/compliance` attempts real tool discovery and returns `configured`, `attempted`, `connected`, and discovered tool names. A failed connection is shown honestly. Direct Elasticsearch remains the dependable application memory API.

## Web Requirement

The Cloud Run dashboard is a complete web experience. It works without the Windows bridge and continues to provide:

- Gemini/Elastic/configuration status;
- memory browser and search;
- action and failure history;
- compliance evidence;
- demo commands and safe demo-mode planning.

## Why a Local Bridge Exists

Browsers and Cloud Run cannot directly control a user's Windows apps. The local bridge is required for PC actions. It binds to localhost, requires a pairing token, and accepts only allowlisted operations.

## Multi-User Isolation

Every memory, chat, action, and failure document contains `user_id`. Every normal Elastic query and deletion includes a `user_id` filter. There is no global memory route. Admin routes are disabled by default.

Automated tests prove user A cannot list, search, delete, or clear user B's data.

## What Is New

This submission is a fresh hackathon build centered on:

- Gemini-only reasoning;
- Cloud Run judge dashboard;
- Elastic Super Memory;
- Google ADK plus Elastic MCP;
- strict user isolation;
- authenticated browser-to-localhost bridge execution;
- Elastic action/failure audit trail.

## Judge Test Checklist

1. Save a to-do URL.
2. Search it in Elastic memory.
3. Ask Jarvis to open the saved link.
4. Observe confirmation/safe action planning.
5. Disconnect the bridge and confirm the UI says the action was not executed.
6. Ask what was worked on today.
7. Inspect action and failure logs.
8. Open `/api/compliance` and verify MCP discovery status.
9. Change the demo `user_id` and verify memories do not cross users.

## Source References

- Devpost rules: `https://rapid-agent.devpost.com/rules`
- Elastic resources: `https://rapid-agent.devpost.com/details/elastic-resources`
- Elastic MCP server: `https://www.elastic.co/docs/explore-analyze/ai-features/agent-builder/mcp-server`
- Google ADK MCP tools: `https://google.github.io/adk-docs/tools-custom/mcp-tools/`
- Cloud Run source deploy: `https://cloud.google.com/run/docs/deploying-source-code`
