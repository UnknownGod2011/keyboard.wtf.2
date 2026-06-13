# keyboard.wtf - Jarvis for your PC

keyboard.wtf is a Gemini-powered real-life second brain for your computer. It remembers useful context with Elastic, plans safe actions, and uses a paired Windows desktop bridge to control supported apps.

This repository is the fresh submission build for the Google Cloud Rapid Agent Hackathon, targeting the Elastic sponsor track.

Hosted dashboard: https://keyboard-wtf-agent-866230084016.asia-south1.run.app

## Winning Story

- Gemini is the brain.
- Elastic is the super memory.
- Google Cloud Run is the hosted judge-facing dashboard.
- The local desktop bridge is the hands that safely act on the computer.

## What Judges Can Do

- Save, search, list, and delete real Elastic memories.
- Ask what was worked on today.
- Find saved links, actions, and previous failures.
- See exactly which Elastic memories were used in a response.
- Inspect action and failure history.
- Use the full web dashboard while the desktop bridge is offline.
- Pair the Windows app and run allowlisted Chrome, app, URL, Gmail draft, clipboard, window, and desktop actions.
- Verify Elastic MCP tool discovery through the Google ADK `MCPToolset`.

## Architecture

```text
Cloud Run dashboard/backend
  |-- Gemini agent: retrieves context, reasons, and creates a structured plan
  |-- Elastic: memories, chats, actions, failures, and user-scoped search
  |-- Elastic MCP: authenticated ADK tool discovery against Kibana
  |
Browser on the demo PC
  |-- direct authenticated request to http://localhost:8787
  |
Windows tray app
  |-- voice trigger, Gemini Live, confirmations, and allowlisted PC actions
```

Cloud Run cannot directly access a judge's localhost. The browser talks directly to the paired local bridge, then reports the verified result to the Cloud Run API for Elastic logging. If the bridge is offline, the UI clearly enters demo mode and never claims the action succeeded.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the detailed flow.

## Tech Stack

- Gemini and Gemini Live
- Google ADK TypeScript `MCPToolset`
- Google Cloud Run and Cloud Build
- Elastic Cloud Serverless on Google Cloud
- Elasticsearch memory/search and Elastic Agent Builder MCP
- Node.js 20, TypeScript, Express
- .NET 8 WinForms, NAudio, Vosk, and local Whisper transcription

The final hackathon model routing is Gemini-only. No non-Google model provider appears in the settings UI or active backend routing.

## Repository Layout

```text
site/
  public/                 Cloud Run dashboard UI
  src/
    config/               Server-side environment validation
    services/actions/     Allowlisted action registry
    services/elastic/     Elasticsearch memory and Elastic MCP
    services/gemini/      Gemini reasoning and memory context
    services/users/       Demo user/device session context
    server.ts             Cloud Run API and static server
  tests/                  Isolation, env, memory, bridge, and registry tests
src/                      Existing Windows tray app and local bridge
```

## Local Web Setup

Prerequisites: Node.js 20 or newer.

```powershell
npm ci
Copy-Item .env.example .env.local
```

Add real values only to `.env.local`. It is ignored by Git.

```powershell
npm run dev
```

Open `http://localhost:8080`.

Useful checks:

```powershell
npm run lint
npm run typecheck
npm test
npm run build
```

## Windows App Setup

For the real desktop-action flow, install the current Windows bridge from the hosted dashboard:

`https://keyboard-wtf-agent-866230084016.asia-south1.run.app/downloads/keyboard-wtf-setup.exe`

For source development, prerequisites are Windows and the .NET 8 SDK.

```powershell
dotnet restore .\KeyboardWtf.sln
dotnet build .\KeyboardWtf.sln
dotnet run --project .\src\KeyboardWtf.csproj
```

The tray app preserves the existing voice trigger, local transcription, Gemini Live conversation, confirmation system, and Windows automation behavior. Open Settings and use the Desktop Bridge section to copy or regenerate the pairing token.

Default shortcuts:

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+K` | Gemini smart writing |
| `Ctrl+Alt+D` | Raw dictation |
| `Ctrl+Alt+Q` | Jarvis mode |
| `Ctrl+Alt+X` | Cancel |
| `Ctrl+Alt+,` | Settings |

## Elastic Setup

Set the Elasticsearch URL and API key server-side. The service creates these indices if missing:

- `keyboard_wtf_memories`
- `keyboard_wtf_actions`
- `keyboard_wtf_chats`
- `keyboard_wtf_failures`

Every document includes `user_id` and `device_id`. Every normal search, count, delete, clear, action lookup, and failure lookup is filtered by `user_id`.

See [ELASTIC_SETUP.md](ELASTIC_SETUP.md).

## Elastic MCP and Google Agent Platform

The backend uses the official Google ADK TypeScript `MCPToolset` with:

- transport: Streamable HTTP
- endpoint: `{KIBANA_URL}/api/agent_builder/mcp`
- auth: `Authorization: ApiKey <ELASTIC_MCP_API_KEY>`

`GET /api/compliance` performs real MCP tool discovery and reports the result. Direct Elasticsearch APIs remain the reliable memory path.

See [GOOGLE_CLOUD_AGENT_BUILDER_SETUP.md](GOOGLE_CLOUD_AGENT_BUILDER_SETUP.md) and [HACKATHON_COMPLIANCE.md](HACKATHON_COMPLIANCE.md).

## Cloud Run Deployment

Do not put secrets in command history. Create Secret Manager entries first, then deploy:

```powershell
gcloud config set project keyboard-wtf-agent
gcloud services enable run.googleapis.com cloudbuild.googleapis.com secretmanager.googleapis.com artifactregistry.googleapis.com
gcloud run deploy keyboard-wtf-agent --source . --region asia-south1 --allow-unauthenticated --set-env-vars GOOGLE_CLOUD_PROJECT_ID=keyboard-wtf-agent,GOOGLE_CLOUD_LOCATION=asia-south1,GOOGLE_CLOUD_RUN_REGION=asia-south1,GOOGLE_CLOUD_RUN_SERVICE_NAME=keyboard-wtf-agent,GEMINI_MODEL=gemini-3.1-pro-preview,DEFAULT_USER_ID=tanushshah2006,DEFAULT_DEVICE_ID=tanush-windows-demo --set-secrets GEMINI_API_KEY=GEMINI_API_KEY:latest,ELASTICSEARCH_API_KEY=ELASTICSEARCH_API_KEY:latest,ELASTIC_MCP_API_KEY=ELASTIC_MCP_API_KEY:latest
```

Configure the non-secret Elastic URLs either as Cloud Run environment variables or in the console. Exact steps are in [GOOGLE_CLOUD_RUN_DEPLOY.md](GOOGLE_CLOUD_RUN_DEPLOY.md).

Current deployed service:

`https://keyboard-wtf-agent-866230084016.asia-south1.run.app`

## Safety and Privacy

- Gemini and Elastic keys are server-side only.
- The bridge listens on localhost only and requires a bearer pairing token.
- Model output can select only allowlisted actions.
- Risky actions require explicit confirmation.
- Gmail drafts are opened for review and are never sent automatically.
- No arbitrary shell command execution exists.
- Sensitive memories are excluded from retrieval unless explicitly requested.
- Demo user selection is not full authentication and is labeled accordingly.

See [SECURITY_AND_PRIVACY.md](SECURITY_AND_PRIVACY.md).

## Demo

The submission-ready 3-minute narration and click path are in [DEMO_SCRIPT.md](DEMO_SCRIPT.md).

## Documentation

- [HACKATHON_COMPLIANCE.md](HACKATHON_COMPLIANCE.md)
- [ELASTIC_SETUP.md](ELASTIC_SETUP.md)
- [GOOGLE_CLOUD_AGENT_BUILDER_SETUP.md](GOOGLE_CLOUD_AGENT_BUILDER_SETUP.md)
- [GOOGLE_CLOUD_RUN_DEPLOY.md](GOOGLE_CLOUD_RUN_DEPLOY.md)
- [DEMO_SCRIPT.md](DEMO_SCRIPT.md)
- [ARCHITECTURE.md](ARCHITECTURE.md)
- [SECURITY_AND_PRIVACY.md](SECURITY_AND_PRIVACY.md)
- [LOCAL_BRIDGE_SETUP.md](LOCAL_BRIDGE_SETUP.md)
- [TESTING.md](TESTING.md)

## License

MIT. See [LICENSE](LICENSE).
