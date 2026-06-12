import { MCPToolset } from "@google/adk";
import type { AppEnv } from "../../config/env.js";

export type ElasticMcpStatus = {
  configured: boolean;
  attempted: boolean;
  connected: boolean;
  message: string;
  toolNames: string[];
};

export async function discoverElasticMcpTools(env: AppEnv): Promise<ElasticMcpStatus> {
  if (!env.elasticAgentBuilderMcpUrl || !env.elasticMcpApiKey) {
    return {
      configured: false,
      attempted: false,
      connected: false,
      message: "Elastic MCP is not configured. Add ELASTIC_AGENT_BUILDER_MCP_URL and ELASTIC_MCP_API_KEY.",
      toolNames: []
    };
  }

  let toolset: MCPToolset | undefined;
  try {
    toolset = new MCPToolset({
      type: "StreamableHTTPConnectionParams",
      url: env.elasticAgentBuilderMcpUrl,
      timeout: 10_000,
      sseReadTimeout: 10_000,
      terminateOnClose: true,
      transportOptions: {
        requestInit: {
          headers: {
            Authorization: `ApiKey ${env.elasticMcpApiKey}`
          }
        }
      }
    });
    const tools = await toolset.getTools();

    return {
      configured: true,
      attempted: true,
      connected: true,
      message: `Elastic MCP connected. Discovered ${tools.length} tools.`,
      toolNames: tools.map((tool) => tool.name ?? "unnamed_tool")
    };
  } catch (error) {
    return {
      configured: true,
      attempted: true,
      connected: false,
      message:
        error instanceof Error
          ? `Elastic MCP discovery failed through Google ADK: ${redact(error.message, env.elasticMcpApiKey)}`
          : "Elastic MCP discovery failed through Google ADK.",
      toolNames: []
    };
  } finally {
    await toolset?.close().catch(() => undefined);
  }
}

function redact(message: string, secret: string): string {
  return secret ? message.replaceAll(secret, "[redacted]") : message;
}
