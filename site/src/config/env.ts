import { config as loadDotEnv } from "dotenv";

loadDotEnv({ path: ".env.local" });
loadDotEnv({ path: ".env" });

export type AppEnv = {
  nodeEnv: string;
  port: number;
  geminiApiKey?: string;
  geminiModel: string;
  googleCloudProjectId: string;
  googleCloudLocation: string;
  googleCloudRunRegion: string;
  googleCloudRunServiceName: string;
  elasticsearchUrl?: string;
  elasticsearchApiKey?: string;
  kibanaUrl?: string;
  elasticAgentBuilderMcpUrl?: string;
  elasticMcpApiKey?: string;
  elasticIndexMemories: string;
  elasticIndexActions: string;
  elasticIndexChats: string;
  elasticIndexFailures: string;
  defaultUserId: string;
  defaultDeviceId: string;
  localBridgePort: number;
  webDashboardOrigin: string;
  enableAdminRoutes: boolean;
};

export type EnvValidation = {
  ok: boolean;
  missingForFullDemo: string[];
  warnings: string[];
};

const DEFAULTS = {
  geminiModel: "gemini-3.1-pro-preview",
  googleCloudProjectId: "keyboard-wtf-agent",
  googleCloudLocation: "asia-south1",
  googleCloudRunRegion: "asia-south1",
  googleCloudRunServiceName: "keyboard-wtf-agent",
  elasticIndexMemories: "keyboard_wtf_memories",
  elasticIndexActions: "keyboard_wtf_actions",
  elasticIndexChats: "keyboard_wtf_chats",
  elasticIndexFailures: "keyboard_wtf_failures",
  defaultUserId: "tanushshah2006",
  defaultDeviceId: "tanush-windows-demo",
  localBridgePort: 8787,
  webDashboardOrigin: "http://localhost:8080"
};

function asNumber(value: string | undefined, fallback: number): number {
  if (!value) return fallback;
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function asBool(value: string | undefined): boolean {
  return value === "1" || value?.toLowerCase() === "true";
}

function clean(value: string | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

export function loadEnv(source: NodeJS.ProcessEnv = process.env): AppEnv {
  return {
    nodeEnv: clean(source.NODE_ENV) ?? "development",
    port: asNumber(source.PORT, 8080),
    geminiApiKey: clean(source.GEMINI_API_KEY),
    geminiModel: clean(source.GEMINI_MODEL) ?? DEFAULTS.geminiModel,
    googleCloudProjectId: clean(source.GOOGLE_CLOUD_PROJECT_ID) ?? DEFAULTS.googleCloudProjectId,
    googleCloudLocation: clean(source.GOOGLE_CLOUD_LOCATION) ?? DEFAULTS.googleCloudLocation,
    googleCloudRunRegion: clean(source.GOOGLE_CLOUD_RUN_REGION) ?? DEFAULTS.googleCloudRunRegion,
    googleCloudRunServiceName:
      clean(source.GOOGLE_CLOUD_RUN_SERVICE_NAME) ?? DEFAULTS.googleCloudRunServiceName,
    elasticsearchUrl: clean(source.ELASTICSEARCH_URL),
    elasticsearchApiKey: clean(source.ELASTICSEARCH_API_KEY),
    kibanaUrl: clean(source.KIBANA_URL),
    elasticAgentBuilderMcpUrl: clean(source.ELASTIC_AGENT_BUILDER_MCP_URL),
    elasticMcpApiKey: clean(source.ELASTIC_MCP_API_KEY),
    elasticIndexMemories: clean(source.ELASTIC_INDEX_MEMORIES) ?? DEFAULTS.elasticIndexMemories,
    elasticIndexActions: clean(source.ELASTIC_INDEX_ACTIONS) ?? DEFAULTS.elasticIndexActions,
    elasticIndexChats: clean(source.ELASTIC_INDEX_CHATS) ?? DEFAULTS.elasticIndexChats,
    elasticIndexFailures: clean(source.ELASTIC_INDEX_FAILURES) ?? DEFAULTS.elasticIndexFailures,
    defaultUserId: clean(source.DEFAULT_USER_ID) ?? DEFAULTS.defaultUserId,
    defaultDeviceId: clean(source.DEFAULT_DEVICE_ID) ?? DEFAULTS.defaultDeviceId,
    localBridgePort: asNumber(source.LOCAL_BRIDGE_PORT, DEFAULTS.localBridgePort),
    webDashboardOrigin: clean(source.WEB_DASHBOARD_ORIGIN) ?? DEFAULTS.webDashboardOrigin,
    enableAdminRoutes: asBool(source.ENABLE_ADMIN_ROUTES)
  };
}

export function validateEnv(env: AppEnv): EnvValidation {
  const missingForFullDemo: string[] = [];
  const warnings: string[] = [];

  if (!env.geminiApiKey) missingForFullDemo.push("GEMINI_API_KEY");
  if (!env.elasticsearchUrl) missingForFullDemo.push("ELASTICSEARCH_URL");
  if (!env.elasticsearchApiKey) missingForFullDemo.push("ELASTICSEARCH_API_KEY");
  if (!env.elasticAgentBuilderMcpUrl) missingForFullDemo.push("ELASTIC_AGENT_BUILDER_MCP_URL");
  if (!env.elasticMcpApiKey) missingForFullDemo.push("ELASTIC_MCP_API_KEY");

  for (const [name, value] of [
    ["ELASTICSEARCH_URL", env.elasticsearchUrl],
    ["KIBANA_URL", env.kibanaUrl],
    ["ELASTIC_AGENT_BUILDER_MCP_URL", env.elasticAgentBuilderMcpUrl],
    ["WEB_DASHBOARD_ORIGIN", env.webDashboardOrigin]
  ] as const) {
    if (!value) continue;
    try {
      new URL(value);
    } catch {
      warnings.push(`${name} is not a valid URL.`);
    }
  }

  return {
    ok: missingForFullDemo.length === 0 && warnings.length === 0,
    missingForFullDemo,
    warnings
  };
}

export function publicConfig(env: AppEnv, validation = validateEnv(env)) {
  return {
    googleCloudProjectId: env.googleCloudProjectId,
    googleCloudLocation: env.googleCloudLocation,
    googleCloudRunRegion: env.googleCloudRunRegion,
    googleCloudRunServiceName: env.googleCloudRunServiceName,
    geminiModel: env.geminiModel,
    geminiConfigured: Boolean(env.geminiApiKey),
    elasticConfigured: Boolean(env.elasticsearchUrl && env.elasticsearchApiKey),
    elasticsearchHost: safeHost(env.elasticsearchUrl),
    kibanaHost: safeHost(env.kibanaUrl),
    elasticMcpConfigured: Boolean(env.elasticAgentBuilderMcpUrl && env.elasticMcpApiKey),
    elasticAgentBuilderMcpHost: safeHost(env.elasticAgentBuilderMcpUrl),
    indices: {
      memories: env.elasticIndexMemories,
      actions: env.elasticIndexActions,
      chats: env.elasticIndexChats,
      failures: env.elasticIndexFailures
    },
    defaultUserId: env.defaultUserId,
    defaultDeviceId: env.defaultDeviceId,
    localBridgePort: env.localBridgePort,
    validation
  };
}

function safeHost(value: string | undefined): string {
  if (!value) return "";
  try {
    return new URL(value).host;
  } catch {
    return "";
  }
}
