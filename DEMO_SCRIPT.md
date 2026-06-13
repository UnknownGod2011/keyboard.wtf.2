# Three-Minute Demo Script

Dashboard: `https://keyboard-wtf-agent-866230084016.asia-south1.run.app`

## 0:00-0:20 - Intro

"keyboard.wtf is Jarvis for your PC. Gemini is the brain, Elastic is the super memory, Cloud Run is the judge-facing web app, and the local Windows bridge is the hands."

Show the four status cards and the browser-generated active user. Mention that memories are scoped to that `user_id`, while production authentication is future work.

## 0:20-0:50 - Remember Context

Use:

`Remember this as my to-do link: https://example.com/todo`

Show the new `link_memory` in Elastic Super Memory. Point out `user_id`, device, type, and timestamp.

## 0:50-1:20 - Recall and Open

Use:

`Open my to-do link.`

Show the "Used N relevant memories from Elastic" indicator and the structured `open_url` plan. Click **Connect Windows Desktop** and approve the local pairing request before showing the bridge open the saved URL. If the bridge is intentionally offline, show the honest "Action was not executed" demo-mode state.

## 1:20-1:50 - Daily Recall

Use:

`What did I work on today?`

Show the user-scoped Elastic retrieval and concise summary. Briefly switch the demo user to show that another user's memories are not returned, then switch back.

## 1:50-2:20 - PC Action

Show the connected device and available allowlisted actions. Bring Chrome to the foreground and use:

`Open a new tab.`

Then use:

`Close this tab.`

Show that close requires confirmation. Optionally open VS Code or File Explorer. Do not spend demo time on OBS unless it is already configured.

## 2:20-2:45 - Elastic Dashboard

Show:

- memory search and filters;
- action history;
- failure history;
- delete and clear confirmation;
- Elastic connection and index names;
- active user and device.

## 2:45-3:00 - Compliance Close

"Gemini creates the plan. Google ADK connects to Elastic Agent Builder MCP. Elasticsearch stores and retrieves the real memory. Cloud Run provides the hosted web requirement, and the authenticated localhost bridge safely performs only allowlisted PC actions."

Open the compliance panel or `/api/compliance` and finish on the architecture diagram.
