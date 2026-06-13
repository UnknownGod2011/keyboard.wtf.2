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
- Receive a private browser-local `user_id` on first visit, with the demo user available only as an explicit option.

## Architecture

```text
Cloud Run dashboard/backend
  |-- Gemini on Vertex AI: retrieves context, reasons, and creates a structured plan
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

Add real values only to `.env.local`. It is ignored by Git. Local development can use `GEMINI_API_KEY`; Cloud Run uses Vertex AI with its runtime service account.

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

The tray app preserves the existing voice trigger, local transcription, Gemini Live conversation, confirmation system, and Windows automation behavior. The dashboard's **Connect Windows Desktop** button detects the local app and requests permission. Copy/paste pairing remains available as a fallback.

### Using Jarvis as a Voice Assistant

The Cloud Run website is the dashboard and memory console. The actual Jarvis voice assistant runs in the Windows tray app:

1. Install and run the Windows bridge.
2. Press `Ctrl+Alt+Q` or choose **Jarvis mode** from the tray menu.
3. The Jarvis overlay appears at the top of the Windows desktop, not inside the browser.
4. Speak naturally. The desktop app handles the microphone, Gemini Live conversation, confirmations, and local app actions.
5. The Cloud Run page stays useful for Elastic memory, logs, status, and sending paired bridge requests.

You can open the app from Start/Desktop after installing it. To close it, use the tray icon menu and choose **Exit**. Double-clicking the tray icon opens local settings.

Default shortcuts:

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+K` | Gemini smart writing |
| `Ctrl+Alt+D` | Raw dictation |
| `Ctrl+Alt+Q` | Jarvis mode |
| `Ctrl+Alt+X` | Cancel |
| `Ctrl+Alt+,` | Settings |

Cloud-triggered actions are intentionally narrower than local voice Jarvis actions. From the Cloud Run dashboard, remote requests go through a P0/P1 allowlist such as open URL, browser tabs, Gmail draft, clipboard, window switch, and show desktop. The installed desktop app can still perform the broader local Jarvis voice workflows it already supported.

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

Do not put secrets in command history. The hardened script checks for each secret, creates it when missing, adds a version from `.env.local` or hidden input, grants the existing runtime account, and redeploys the same service:

```powershell
.\scripts\configure-cloud-run.ps1
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
- Each fresh browser receives a local `user_id` and `device_id`; all Elastic access is scoped to that ID.
- Browser/demo user selection is not full authentication and is labeled accordingly.

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
