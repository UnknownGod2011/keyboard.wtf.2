# Security and Privacy

## Secrets

- `.env`, `.env.local`, credential JSON, service-account JSON, and local key files are ignored.
- `.env.example` contains placeholders only.
- Gemini and Elastic credentials are read only by the Node server.
- Public status responses expose hostnames and configuration state, never keys.
- Desktop Gemini keys and the bridge token are protected with Windows DPAPI.
- Error messages redact the Elastic MCP API key.

## Local Bridge

- Binds only to `http://localhost`.
- Rejects non-local requests.
- Requires `Authorization: Bearer <pairing token>`.
- Uses an explicit CORS origin allowlist.
- Supports token regeneration.
- Accepts only actions in `LocalBridgeActionRegistry`.

## Action Safety

Each action has:

- a stable name;
- mapped parameters;
- safety level;
- confirmation requirement;
- Windows-only platform support.

Closing a browser tab and preparing a Gmail draft require confirmation. The product never sends email, deletes files, purchases items, or submits forms automatically. No generic shell tool exists.

## Memory Privacy

- Normal and private memory can be retrieved when relevant.
- Sensitive memory is excluded unless the caller explicitly opts in.
- Memory saving can be disabled in the dashboard.
- Declared memories are not saved while memory saving is disabled.
- Chat summaries are saved only when useful, not for every tiny exchange.
- All data access is scoped to `user_id`.

## Demo User Limitation

The user selector is a demo session abstraction, not authentication. Deploy one trusted demo user per public hackathon instance. A production release should add Google Identity Platform or another authenticated user-to-session mapping before accepting arbitrary public users.

## Logging

Do not log raw credentials, request authorization headers, or secret environment values. Action logs contain action name, status, safety metadata, device, user, and non-secret detail.
