# Google Cloud Run Deployment

Do not deploy until the local test suite passes. Do not commit `.env.local`.

Current service URL:

`https://keyboard-wtf-agent-866230084016.asia-south1.run.app`

Canonical submitted service: `keyboard-wtf-agent`

## 1. Select the Project

```powershell
gcloud auth login
gcloud config set project keyboard-wtf-agent
gcloud services enable run.googleapis.com cloudbuild.googleapis.com secretmanager.googleapis.com artifactregistry.googleapis.com
```

## 2. Create Secrets

Use `scripts/configure-cloud-run.ps1`. It checks whether each secret exists before creation, adds a version without putting the value on the command line, grants the existing runtime service account, and attaches it to the same service.

- `GEMINI_API_KEY`
- `ELASTICSEARCH_API_KEY`
- `ELASTIC_MCP_API_KEY`

Grant the Cloud Run runtime service account Secret Manager Secret Accessor.

## 3. Configure Non-Secret Variables

Use these environment variables:

```text
GOOGLE_CLOUD_PROJECT_ID=keyboard-wtf-agent
GOOGLE_CLOUD_LOCATION=asia-south1
GOOGLE_CLOUD_RUN_REGION=asia-south1
GOOGLE_CLOUD_RUN_SERVICE_NAME=keyboard-wtf-agent
GEMINI_MODEL=gemini-3.1-pro-preview
GOOGLE_GENAI_USE_VERTEXAI=true
GEMINI_VERTEX_LOCATION=global
ELASTICSEARCH_URL=<your Elasticsearch URL>
KIBANA_URL=<your Kibana URL>
ELASTIC_AGENT_BUILDER_MCP_URL=<your Kibana URL>/api/agent_builder/mcp
DEFAULT_USER_ID=tanushshah2006
DEFAULT_DEVICE_ID=tanush-windows-demo
```

## 4. Deploy from Source

```powershell
.\scripts\configure-cloud-run.ps1
```

Cloud Run prefers Vertex AI with Application Default Credentials. `GEMINI_API_KEY` remains available for the desktop/local fallback and hackathon configuration, but it is never exposed to the browser.

## 5. Alternative Container Flow

```powershell
gcloud builds submit --tag gcr.io/keyboard-wtf-agent/keyboard-wtf-agent
gcloud run deploy keyboard-wtf-agent --image gcr.io/keyboard-wtf-agent/keyboard-wtf-agent --region asia-south1 --allow-unauthenticated
```

## 6. Verify

```powershell
gcloud run services describe keyboard-wtf-agent --region asia-south1 --format="value(status.url)"
```

Open the returned URL and check:

- `/api/health`;
- Gemini configured;
- Elastic connected;
- MCP discovery status;
- memory create/search/delete;
- bridge offline message;
- no secrets in browser network responses.

Add the returned HTTPS URL to Devpost and README.
