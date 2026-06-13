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
