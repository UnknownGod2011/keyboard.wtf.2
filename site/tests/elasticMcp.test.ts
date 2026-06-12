import { describe, expect, it } from "vitest";
import { loadEnv } from "../src/config/env.js";
import { discoverElasticMcpTools } from "../src/services/elastic/elasticMcpClient.js";

describe("Elastic MCP", () => {
  it("stays honest and does not attempt a connection without server credentials", async () => {
    const status = await discoverElasticMcpTools(loadEnv({}));

    expect(status.configured).toBe(false);
    expect(status.attempted).toBe(false);
    expect(status.connected).toBe(false);
    expect(status.toolNames).toEqual([]);
  });
});
