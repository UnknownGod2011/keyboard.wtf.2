import { describe, expect, it } from "vitest";
import { loadEnv, publicConfig, validateEnv } from "../src/config/env.js";

describe("env validation", () => {
  it("uses safe hackathon defaults and reports missing server secrets", () => {
    const env = loadEnv({});
    const validation = validateEnv(env);
    const publicSafe = publicConfig(env, validation);

    expect(env.googleCloudLocation).toBe("asia-south1");
    expect(env.geminiModel).toBe("gemini-3.1-pro-preview");
    expect(validation.missingForFullDemo).toContain("GEMINI_API_KEY");
    expect(publicSafe).not.toHaveProperty("geminiApiKey");
    expect(JSON.stringify(publicSafe)).not.toContain("API_KEY=");
  });

  it("reports invalid URLs without exposing or throwing on public config", () => {
    const env = loadEnv({ ELASTICSEARCH_URL: "not-a-url" });
    const validation = validateEnv(env);
    const publicSafe = publicConfig(env, validation);

    expect(validation.warnings).toContain("ELASTICSEARCH_URL is not a valid URL.");
    expect(publicSafe.elasticsearchHost).toBe("");
  });
});
