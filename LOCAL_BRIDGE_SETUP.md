# Local Desktop Bridge Setup

## Start the Windows App

```powershell
dotnet run --project .\src\KeyboardWtf.csproj
```

The app starts in the system tray and hosts the bridge at:

`http://localhost:8787`

The port can be changed with `LOCAL_BRIDGE_PORT`.

## Pair the Dashboard

1. Open keyboard.wtf Settings.
2. Select Desktop Bridge.
3. Copy the pairing token.
4. Open the Cloud Run dashboard on the same PC.
5. Paste the token in Settings and save.
6. Click Test Bridge.

The token stays in browser session storage and is sent only to localhost. It is never attached to Cloud Run API requests.

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

Risky actions return `confirmation_required` until the browser resubmits with `confirmed: true`. The existing local confirmation system remains active for normal tray-app use.

## CORS

The official keyboard.wtf Cloud Run origins and local development origins on ports 8080 and 3000 are allowed by default. Set `WEB_DASHBOARD_ORIGIN` only when using a different dashboard origin.

## Offline Behavior

If the app is stopped, unpaired, or unreachable:

- memory/search still work;
- the agent may show the safe plan;
- the action result is `demo_mode`;
- the UI says the action was not executed.
