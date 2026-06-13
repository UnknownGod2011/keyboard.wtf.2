import { describe, expect, it } from "vitest";
import { loadEnv, publicConfig, validateEnv } from "../src/config/env.js";

describe("env validation", () => {
  it("uses safe hackathon defaults and reports missing server secrets", () => {
    const env = loadEnv({});
    const validation = validateEnv(env);
    const publicSafe = publicConfig(env, validation);

    expect(env.googleCloudLocation).toBe("asia-south1");
    expect(env.geminiModel).toBe("gemini-3.1-pro-preview");
    expect(validation.missingForFullDemo).toContain("GEMINI_API_KEY or GOOGLE_GENAI_USE_VERTEXAI=true");
    expect(publicSafe).not.toHaveProperty("geminiApiKey");
    expect(JSON.stringify(publicSafe)).not.toContain("API_KEY=");
  });

  it("supports Cloud Run Vertex AI without exposing or requiring an API key", () => {
    const env = loadEnv({
      GOOGLE_GENAI_USE_VERTEXAI: "true",
      GOOGLE_CLOUD_PROJECT_ID: "keyboard-wtf-agent",
      GEMINI_VERTEX_LOCATION: "global"
    });
    const validation = validateEnv(env);
    const publicSafe = publicConfig(env, validation);

    expect(validation.missingForFullDemo).not.toContain("GEMINI_API_KEY or GOOGLE_GENAI_USE_VERTEXAI=true");
    expect(publicSafe.geminiConfigured).toBe(true);
    expect(publicSafe.geminiProvider).toContain("Vertex AI");
    expect(publicSafe).not.toHaveProperty("geminiApiKey");
  });

  it("reports invalid URLs without exposing or throwing on public config", () => {
    const env = loadEnv({ ELASTICSEARCH_URL: "not-a-url" });
    const validation = validateEnv(env);
    const publicSafe = publicConfig(env, validation);

    expect(validation.warnings).toContain("ELASTICSEARCH_URL is not a valid URL.");
    expect(publicSafe.elasticsearchHost).toBe("");
  });
});
