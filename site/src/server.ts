import express, { type Request } from "express";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { loadEnv, publicConfig, validateEnv } from "./config/env.js";
import { ACTION_REGISTRY } from "./services/actions/actionRegistry.js";
import { ElasticMemoryStore } from "./services/elastic/elasticMemoryStore.js";
import { discoverElasticMcpTools } from "./services/elastic/elasticMcpClient.js";
import { isMemoryType, type MemorySearchOptions, type MemoryType } from "./services/elastic/types.js";
import { GeminiAgent } from "./services/gemini/geminiAgent.js";
import { userContextFromRequest } from "./services/users/userContext.js";

const env = loadEnv();
const validation = validateEnv(env);
const memoryStore = new ElasticMemoryStore(env);
const agent = new GeminiAgent(env, memoryStore);
const app = express();
const __dirname = dirname(fileURLToPath(import.meta.url));

app.disable("x-powered-by");
app.use(express.json({ limit: "1mb" }));
app.use((_, res, next) => {
  res.setHeader("x-content-type-options", "nosniff");
  res.setHeader("referrer-policy", "strict-origin-when-cross-origin");
  next();
});

app.get("/api/health", (_, res) => {
  res.json({ ok: true, service: "keyboard.wtf Agent", region: env.googleCloudRunRegion });
});

app.get("/api/status", async (req, res) => {
  const user = userContextFromRequest(req, env);
  const elastic = await memoryStore.status();
  const memoryCount = await memoryStore.countMemories(user).catch(() => 0);
  res.json({
    ok: true,
    config: publicConfig(env, validation),
    activeUser: user,
    memoryCount,
    elastic,
    gemini: { configured: agent.configured(), model: env.geminiModel },
    bridge: {
      bridgeOnline: false,
      demoMode: true,
      message: "Bridge status is checked directly by the browser against localhost:8787."
    },
    actions: ACTION_REGISTRY,
    note: "Secrets are evaluated server-side and are never returned to the browser."
  });
});

app.post("/api/elastic/test", async (_, res) => {
  try {
    await memoryStore.ensureIndices();
    const status = await memoryStore.status();
    res.json({ ok: status.connected, status });
  } catch (error) {
    res.status(500).json({ ok: false, error: safeError(error) });
  }
});

app.post("/api/gemini/test", async (_, res) => {
  const result = await agent.test();
  res.status(result.ok ? 200 : 500).json(result);
});

app.get("/api/compliance", async (_, res) => {
  const mcp = await discoverElasticMcpTools(env);
  res.json({
    name: "keyboard.wtf - Jarvis for your PC",
    track: "Elastic",
    story: {
      gemini: "Gemini is the reasoning brain.",
      googleCloud: "Google Cloud Run hosts the judge-facing dashboard; authenticated Google ADK MCP discovery is implemented.",
      elastic: "Elasticsearch stores memories, chats, actions, and failures with strict user_id isolation.",
      bridge: "The Windows local bridge is the hands that safely act on apps."
    },
    requirements: [
      "Functional agent that reasons, plans, uses tools, and acts through an allowlisted bridge.",
      "Gemini-only final hackathon build.",
      "Elastic MCP endpoint configured through Google ADK scaffold.",
      "Cloud Run-ready web dashboard.",
      "Public repo, MIT license, and 3-minute demo script."
    ],
    elasticMcp: mcp
  });
});

app.get("/api/memory", async (req, res) => {
  const user = userContextFromRequest(req, env);
  const memories = await memoryStore.listMemories(user, optionsFromRequest(req));
  res.json({ ok: true, user, memories, count: await memoryStore.countMemories(user) });
});

app.post("/api/memory", async (req, res) => {
  try {
    const user = userContextFromRequest(req, env);
    const memory = await memoryStore.saveMemory(user, {
      type: memoryTypeOrDefault(req.body?.type),
      title: stringValue(req.body?.title),
      content: requiredString(req.body?.content, "content"),
      summary: stringValue(req.body?.summary),
      source_app: stringValue(req.body?.source_app),
      source_window: stringValue(req.body?.source_window),
      url: stringValue(req.body?.url),
      tags: Array.isArray(req.body?.tags) ? req.body.tags : [],
      importance: Number(req.body?.importance ?? 5),
      privacy_level: req.body?.privacy_level === "sensitive" ? "sensitive" : req.body?.privacy_level === "private" ? "private" : "normal",
      metadata: objectValue(req.body?.metadata)
    });
    res.json({ ok: true, memory });
  } catch (error) {
    res.status(400).json({ ok: false, error: safeError(error) });
  }
});

app.post("/api/memory/search", async (req, res) => {
  const user = userContextFromRequest(req, env);
  const memories = await memoryStore.searchMemories(user, optionsFromRequest(req));
  res.json({ ok: true, user, memories, memoryIndicator: `Used ${memories.length} relevant memories from Elastic.` });
});

app.delete("/api/memory/:id", async (req, res) => {
  const user = userContextFromRequest(req, env);
  const deleted = await memoryStore.deleteMemory(user, req.params.id);
  res.status(deleted ? 200 : 404).json({ ok: deleted });
});

app.post("/api/memory/clear", async (req, res) => {
  const user = userContextFromRequest(req, env);
  if (req.body?.confirm !== true && req.body?.confirm !== "CLEAR") {
    res.status(400).json({ ok: false, error: "Clear memory requires confirm=true or confirm=CLEAR." });
    return;
  }
  const deleted = await memoryStore.clearMemories(user);
  res.json({ ok: true, deleted, user });
});

app.get("/api/actions", async (req, res) => {
  const user = userContextFromRequest(req, env);
  const query = stringValue(req.query.query);
  const actions = query
    ? await memoryStore.searchActions(user, query, numberValue(req.query.limit, 20))
    : await memoryStore.listActions(user, numberValue(req.query.limit, 20));
  res.json({ ok: true, user, actions });
});

app.get("/api/failures", async (req, res) => {
  const user = userContextFromRequest(req, env);
  const failures = await memoryStore.listFailures(user, numberValue(req.query.limit, 20));
  res.json({ ok: true, user, failures });
});

app.post("/api/actions", async (req, res) => {
  const user = userContextFromRequest(req, env);
  const action = await memoryStore.saveAction(user, {
    action: requiredString(req.body?.action, "action"),
    detail: stringValue(req.body?.detail),
    status: ["completed", "failed", "confirmation_required", "demo_mode", "blocked"].includes(req.body?.status)
      ? req.body.status
      : "failed",
    safety_level: ["safe", "confirm", "blocked"].includes(req.body?.safety_level)
      ? req.body.safety_level
      : "safe",
    requires_confirmation: Boolean(req.body?.requires_confirmation),
    metadata: objectValue(req.body?.metadata)
  });
  res.json({ ok: true, action });
});

app.post("/api/failures", async (req, res) => {
  const user = userContextFromRequest(req, env);
  const failure = await memoryStore.saveFailure(user, {
    action: requiredString(req.body?.action, "action"),
    detail: stringValue(req.body?.detail),
    safety_level: ["safe", "confirm", "blocked"].includes(req.body?.safety_level)
      ? req.body.safety_level
      : "blocked",
    requires_confirmation: Boolean(req.body?.requires_confirmation),
    metadata: objectValue(req.body?.metadata)
  });
  res.json({ ok: true, failure });
});

app.get("/api/bridge/status", (_, res) => {
  res.json({
    ok: true,
    bridgeOnline: false,
    demoMode: true,
    message: "Cloud Run cannot access a user's localhost. The dashboard browser checks the paired bridge directly."
  });
});

app.post("/api/bridge/command", (_, res) => {
  res.status(409).json({
    ok: false,
    bridgeOnline: false,
    demoMode: true,
    message: "Use the dashboard's direct browser-to-localhost bridge connection."
  });
});

app.post("/api/chat", async (req, res) => {
  const user = userContextFromRequest(req, env);
  try {
    const message = requiredString(req.body?.message, "message");
    const memorySavingEnabled = req.body?.memorySavingEnabled !== false;
    let savedMemory = null;
    if (memorySavingEnabled && /\bremember\b/i.test(message)) {
      savedMemory = await agent.remember(user, message);
    }

    const reply = await agent.reply(user, message, { memorySavingEnabled });
    if (reply.plan.action) {
      if (reply.plan.requires_confirmation && !req.body?.confirmed) {
        await memoryStore.saveAction(user, {
          action: reply.plan.action,
          detail: reply.plan.confirmation_message,
          status: "confirmation_required",
          safety_level: "confirm",
          requires_confirmation: true
        });
      }
    }

    res.json({
      ok: true,
      user,
      savedMemory,
      response: reply.response,
      plan: reply.plan,
      bridgeResult: null,
      memoriesUsed: reply.memoriesUsed,
      memoryIndicator: reply.memoryIndicator
    });
  } catch (error) {
    await memoryStore
      .saveFailure(user, {
        action: "chat",
        detail: safeError(error),
        safety_level: "blocked"
      })
      .catch(() => undefined);
    res.status(400).json({ ok: false, error: safeError(error) });
  }
});

app.use(express.static(join(__dirname, "public")));
app.get("*", (_, res) => {
  res.sendFile(join(__dirname, "public", "index.html"));
});

await memoryStore.ensureIndices().catch((error) => {
  console.warn(`Elastic index setup skipped: ${safeError(error)}`);
});

app.listen(env.port, () => {
  console.log(`keyboard.wtf Agent dashboard listening on ${env.port}`);
  if (validation.missingForFullDemo.length) {
    console.log(`Missing for full demo: ${validation.missingForFullDemo.join(", ")}`);
  }
});

function optionsFromRequest(req: Request): MemorySearchOptions {
  const rawTypes = Array.isArray(req.body?.types)
    ? req.body.types
    : Array.isArray(req.query.type)
      ? req.query.type
      : typeof req.query.type === "string"
        ? req.query.type.split(",")
        : [];
  const types = rawTypes.filter((value: unknown): value is MemoryType => typeof value === "string" && isMemoryType(value));
  return {
    query: stringValue(req.body?.query) ?? stringValue(req.query.query),
    types,
    tags: Array.isArray(req.body?.tags) ? req.body.tags.map(String) : [],
    dateFrom: stringValue(req.body?.dateFrom),
    dateTo: stringValue(req.body?.dateTo),
    includeSensitive: req.body?.includeSensitive === true,
    sort: req.body?.sort === "date" ? "date" : "relevance",
    limit: numberValue(req.body?.limit ?? req.query.limit, 20)
  };
}

function memoryTypeOrDefault(value: unknown): MemoryType {
  return typeof value === "string" && isMemoryType(value) ? value : "semantic_memory";
}

function stringValue(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

function requiredString(value: unknown, name: string): string {
  const text = stringValue(value);
  if (!text) throw new Error(`${name} is required.`);
  return text;
}

function objectValue(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value) ? (value as Record<string, unknown>) : {};
}

function numberValue(value: unknown, fallback: number): number {
  const parsed = Number.parseInt(String(value ?? ""), 10);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function safeError(error: unknown): string {
  return error instanceof Error ? error.message : "Unknown error";
}
