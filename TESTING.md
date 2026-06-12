# Testing

## Automated

```powershell
npm ci
npm run lint
npm run typecheck
npm test
npm run build
dotnet restore .\KeyboardWtf.sln
dotnet build .\KeyboardWtf.sln --no-restore
```

Coverage includes:

- environment defaults and validation;
- in-memory Elastic contract CRUD;
- user A/B search, list, delete, and clear isolation;
- action registry safety metadata;
- bridge offline behavior;
- TypeScript compile and production build.

## Connected Integration

With server-side secrets configured:

1. `POST /api/elastic/test`
2. verify four indices exist;
3. save a memory;
4. search as user A;
5. search as user B and expect no result;
6. delete as user B and expect false/404;
7. delete as user A and expect success;
8. save completed and failed action logs;
9. `GET /api/compliance` and inspect MCP discovery;
10. `POST /api/gemini/test`.

## Manual Dashboard

- status cards;
- memory create/search/type filter/delete/clear;
- useful-chat memory toggle;
- action and failure lists;
- bridge offline wording;
- responsive layout;
- no keys in page source or API payloads.

## Manual Bridge

- missing token rejected;
- wrong token rejected;
- correct token returns status;
- open URL;
- Chrome next/previous/new/reopen;
- close tab requires confirmation;
- Gmail draft requires confirmation and does not send;
- File Explorer, Settings, Spotify, and VS Code;
- action failure is logged.

## Cloud Run Readiness

```powershell
docker build -t keyboard-wtf-agent:test .
docker run --rm -p 8080:8080 keyboard-wtf-agent:test
```

Then request `/api/health` and open the dashboard.

## Secret Scan

Before commit:

```powershell
git status --short
git ls-files .env .env.local
```

Also scan tracked files for known key prefixes without printing the matches. `.env.example` must contain placeholders only.

## Latest Verification

- `npm ci`: passed.
- `npm run lint`: passed.
- `npm run typecheck`: passed.
- `npm test`: passed.
- `npm run build`: passed.
- `.NET build`: passed with zero warnings and zero errors.
- Fresh Windows bridge launch: passed.
- Bridge without token: rejected with HTTP 401.
- Bridge with token: passed and returned 14 allowlisted actions.
- Risky bridge action without confirmation: rejected with HTTP 409.
- Dashboard health/status/compliance: passed on local port 8081.
- Responsive Playwright checks: passed at 390x844 and 1440x1000 with no horizontal or button overflow.
- Offline action wording: passed with `demo_mode` and no success claim.
- Docker engine: started successfully.
- Docker image build: blocked while pulling `node:20-slim` by repeated Docker Hub network timeout; the Dockerfile itself reached the base-image resolution step.
- Live Gemini, Elasticsearch, and MCP calls: not run because this machine has no server-side runtime secrets configured.
- `npm audit --omit=dev`: 17 transitive advisories remain under current `@google/adk`; npm offers only a breaking downgrade of ADK.
- Public GitHub repository: reachable, but currently empty; this local implementation has not been committed or pushed.
- Git origin: corrected to `https://github.com/UnknownGod2011/keyboard.wtf.2.git`.
- System Google Cloud CLI installation was unavailable; deployment used an ignored portable SDK in the system temp directory.

## Deployment Verification

- Portable Google Cloud CLI 572.0.0 was used from the system temp directory.
- Google Cloud project `keyboard-wtf-agent` is active.
- Cloud Run, Cloud Build, Artifact Registry, and Secret Manager APIs were enabled.
- Cloud Run source deployment passed using the repository Dockerfile.
- Deployed revision: `keyboard-wtf-agent-00001-d86`.
- Hosted URL: `https://keyboard-wtf-agent-866230084016.asia-south1.run.app`.
- Public `/api/health`: passed.
- Public dashboard HTML: passed.
- Public bridge-offline/demo behavior: passed.
