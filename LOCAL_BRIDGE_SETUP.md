# Local Desktop Bridge Setup

## Start the Windows App

Download the current installer from the Cloud Run dashboard or directly from:

`https://keyboard-wtf-agent-866230084016.asia-south1.run.app/downloads/keyboard-wtf-setup.exe`

For source development:

```powershell
dotnet run --project .\src\KeyboardWtf.csproj
```

The app starts in the system tray and hosts the bridge at:

`http://localhost:8787`

The port can be changed with `LOCAL_BRIDGE_PORT`.

Double-click the tray icon to open local settings. Use the tray menu to start **Jarvis mode**, open voice notes, cancel the current operation, or choose **Exit** to close the app.

## Voice Jarvis and Overlay

The Cloud Run dashboard includes a triggered **Talk to Jarvis** panel. In current desktop Chrome, it can listen after a user click, send the transcript to the server-side Gemini + Elastic agent, speak the answer, and continue turn by turn until **Stop** is pressed. Chrome may use its own speech service for transcription.

The installed Windows app remains the full desktop voice experience:

- Press `Ctrl+Alt+Q` or choose **Jarvis mode** from the tray icon.
- The Jarvis popup appears at the top of the Windows desktop.
- The popup shows listening, transcribing, thinking, executing, speaking, done, cancelled, and error states.
- `Ctrl+Alt+X` cancels the current voice operation.
- The desktop app handles microphone capture, local transcription, Gemini Live voice conversation, confirmations, and real PC actions.

The website voice panel appears inside the browser. It cannot draw over other Windows apps. The installed Windows app owns the always-on-top desktop overlay, Gemini Live audio stream, tray controls, and broader local action set.

The desktop Gemini Live path needs a Gemini API key saved in local Settings. The key is protected locally and is never copied from the Cloud Run service. Users who do not want to configure a desktop key can still use the website voice panel with server-side Gemini, then pair the bridge for allowlisted PC actions.

## Pair the Dashboard

1. Open the Cloud Run dashboard on the Windows PC.
2. Click **Connect Windows Desktop**.
3. If the bridge is installed and running, approve the Windows permission prompt.
4. The token is returned only to that browser session and available actions appear automatically.
5. If automatic pairing is unavailable, open keyboard.wtf Settings, copy the token, paste it into dashboard Settings, and connect again.

The token stays in browser session storage and is sent only to localhost. It is never attached to Cloud Run API requests. Pairing accepts only the canonical Cloud Run and local development origins and requires an on-device approval prompt.

## Supported P0/P1 Actions

- open URL;
- open known app;
- next, previous, new, reopen, and close browser tab;
- Google and YouTube search;
- open a Gmail draft for review;
- copy text;
- switch window;
- show desktop.

The bridge maps these public action names to the existing Windows command registry. It never executes model-supplied shell commands.

Local voice Jarvis can still use the existing desktop-only workflows exposed by the Windows app. Cloud-triggered actions intentionally stay on the allowlist above so a hosted page cannot ask the PC to do arbitrary things.

## Confirmation

Risky actions return `confirmation_required` until the browser confirms, then require a second on-device approval before execution. The existing local confirmation system remains active for normal tray-app use.

## CORS

The official keyboard.wtf Cloud Run origins and local development origins on ports 8080 and 3000 are allowed by default. Set `WEB_DASHBOARD_ORIGIN` only when using a different dashboard origin.

## Offline Behavior

If the app is stopped, unpaired, or unreachable:

- memory/search still work;
- the agent may show the safe plan;
- the action result is `demo_mode`;
- the UI says the action was not executed.
