import { GoogleGenAI } from "@google/genai";
import type { AppEnv } from "../../config/env.js";
import { registryForPrompt, getActionDefinition } from "../actions/actionRegistry.js";
import type { MemoryDocument, MemoryStore } from "../elastic/types.js";
import type { UserContext } from "../users/userContext.js";

export type AgentPlan = {
  intent: string;
  response: string;
  memory_query: string;
  required_memory_types: string[];
  action: string | null;
  params: Record<string, unknown>;
  safety_level: "safe" | "confirm" | "blocked";
  requires_confirmation: boolean;
  confirmation_message: string;
};

export type AgentReply = {
  response: string;
  plan: AgentPlan;
  memoriesUsed: MemoryDocument[];
  memoryIndicator: string;
};

export type AgentReplyOptions = {
  memorySavingEnabled?: boolean;
};

const SYSTEM_PROMPT = `
You are keyboard.wtf, Jarvis for PC.
Gemini is the reasoning brain. Elastic is the super memory. The local desktop bridge is the only hand that can act on the PC.
Use Elastic memory only when relevant. Do not invent memories.
Return concise action-oriented answers.
Never expose secrets or internal keys.
Never claim a desktop action succeeded unless the local bridge confirms it.
Never output shell commands for execution. Only choose from the allowlisted action registry.
Ask confirmation for risky actions.

Allowlisted actions:
${registryForPrompt()}
`;

const EMPTY_PLAN: AgentPlan = {
  intent: "chat",
  response: "",
  memory_query: "",
  required_memory_types: [],
  action: null,
  params: {},
  safety_level: "safe",
  requires_confirmation: false,
  confirmation_message: ""
};

export class GeminiAgent {
  private readonly ai?: GoogleGenAI;

  constructor(
    private readonly env: AppEnv,
    private readonly memoryStore: MemoryStore
  ) {
    if (env.geminiApiKey) {
      this.ai = new GoogleGenAI({ apiKey: env.geminiApiKey });
    }
  }

  configured(): boolean {
    return Boolean(this.ai);
  }

  async test(): Promise<{ ok: boolean; message: string }> {
    if (!this.ai) return { ok: false, message: "GEMINI_API_KEY is not configured server-side." };
    try {
      const response = await this.ai.models.generateContent({
        model: this.env.geminiModel,
        contents: "Reply with exactly OK."
      });
      return { ok: true, message: response.text?.trim() || "Gemini responded." };
    } catch (error) {
      return { ok: false, message: error instanceof Error ? error.message : "Gemini test failed." };
    }
  }

  async reply(user: UserContext, message: string, options: AgentReplyOptions = {}): Promise<AgentReply> {
    const relevant = await this.retrieveRelevantMemories(user, message);

    const plan = this.ai
      ? await this.planWithGemini(message, relevant)
      : this.planWithHeuristics(message, relevant);

    const safePlan = this.sanitizePlan(plan);
    const memoryIndicator = `Used ${relevant.length} relevant ${
      relevant.length === 1 ? "memory" : "memories"
    } from Elastic.`;

    if (options.memorySavingEnabled !== false && this.shouldSaveChat(message, safePlan, relevant)) {
      await this.memoryStore.saveChat(user, {
        role: "user",
        content: message,
        summary: message.slice(0, 240),
        memory_used_count: relevant.length
      });
      await this.memoryStore.saveChat(user, {
        role: "assistant",
        content: safePlan.response,
        summary: safePlan.response.slice(0, 240),
        memory_used_count: relevant.length,
        metadata: { plan: safePlan }
      });
    }

    return {
      response: safePlan.response,
      plan: safePlan,
      memoriesUsed: relevant,
      memoryIndicator
    };
  }

  private async retrieveRelevantMemories(user: UserContext, message: string): Promise<MemoryDocument[]> {
    const lower = message.toLowerCase();
    if (lower.includes("what did i work on today")) {
      const today = new Date();
      today.setHours(0, 0, 0, 0);
      return this.memoryStore.listMemories(user, {
        dateFrom: today.toISOString(),
        limit: 10,
        sort: "date"
      });
    }
    if (lower.includes("saved actions") || lower.includes("action history")) {
      return this.memoryStore.searchActions(user, message, 10);
    }
    if (lower.includes("failed last time") || lower.includes("last failure")) {
      return this.memoryStore.listFailures(user, 5);
    }
    return this.memoryStore.searchMemories(user, {
      query: message,
      limit: 5,
      sort: "relevance"
    });
  }

  async remember(user: UserContext, message: string): Promise<MemoryDocument> {
    const link = message.match(/https?:\/\/\S+/)?.[0]?.replace(/[),.]+$/, "") ?? "";
    const type = link ? "link_memory" : "semantic_memory";
    const title = this.memoryTitle(message, type);
    return this.memoryStore.saveMemory(user, {
      type,
      title,
      content: message,
      summary: message.slice(0, 240),
      url: link,
      tags: this.tagsFromText(message)
    });
  }

  private async planWithGemini(message: string, memories: MemoryDocument[]): Promise<AgentPlan> {
    if (!this.ai) return this.planWithHeuristics(message, memories);
    const memoryContext = memories.length
      ? memories
          .map((memory, index) => `${index + 1}. ${memory.type}: ${memory.title} | ${memory.summary} | ${memory.url}`)
          .join("\n")
      : "No relevant Elastic memories.";

    const prompt = `${SYSTEM_PROMPT}

Elastic memories:
${memoryContext}

User command:
${message}

Return only compact JSON with:
intent, response, memory_query, required_memory_types, action, params, safety_level, requires_confirmation, confirmation_message.
`;

    try {
      const response = await this.ai.models.generateContent({
        model: this.env.geminiModel,
        contents: prompt,
        config: {
          temperature: 0.2,
          responseMimeType: "application/json"
        }
      });
      return JSON.parse(response.text ?? "{}") as AgentPlan;
    } catch {
      return this.planWithHeuristics(message, memories);
    }
  }

  private planWithHeuristics(message: string, memories: MemoryDocument[]): AgentPlan {
    const lower = message.toLowerCase();
    if (lower.includes("remember")) {
      return {
        ...EMPTY_PLAN,
        intent: "save_memory",
        response: "I saved that to Elastic Super Memory.",
        memory_query: message,
        required_memory_types: ["semantic_memory", "link_memory"]
      };
    }

    if (lower.includes("open") && lower.includes("link")) {
      const memory = memories.find((item) => item.url) ?? memories[0];
      if (memory?.url) {
        return {
          ...EMPTY_PLAN,
          intent: "open_saved_link",
          response: `I found "${memory.title}" in Elastic. I can open it through the desktop bridge.`,
          memory_query: message,
          required_memory_types: ["link_memory", "semantic_memory"],
          action: "open_url",
          params: { url: memory.url },
          safety_level: "safe"
        };
      }
      return { ...EMPTY_PLAN, intent: "missing_memory", response: "I could not find a saved link for that." };
    }

    if (lower.includes("what did i work on today")) {
      const summary = memories.length
        ? memories.map((memory) => `- ${memory.title}`).join("\n")
        : "I do not have enough saved Elastic memory for today yet.";
      return { ...EMPTY_PLAN, intent: "daily_summary", response: summary, memory_query: message };
    }

    if (lower.includes("failed last time")) {
      return {
        ...EMPTY_PLAN,
        intent: "failure_lookup",
        response: memories.length
          ? `The most relevant failure memory is: ${memories[0].summary}`
          : "I do not see a matching failure memory yet.",
        memory_query: message,
        required_memory_types: ["failure_memory"]
      };
    }

    if (lower.includes("saved actions") || lower.includes("action history")) {
      return {
        ...EMPTY_PLAN,
        intent: "action_history",
        response: memories.length
          ? memories.map((memory) => `- ${memory.summary}`).join("\n")
          : "I do not see any matching saved actions yet.",
        memory_query: message,
        required_memory_types: ["action_memory"]
      };
    }

    return {
      ...EMPTY_PLAN,
      intent: "chat",
      response:
        memories.length > 0
          ? `I found relevant Elastic context and can use it: ${memories[0].summary}`
          : "I am ready. Ask me to remember something, search memory, or run a safe desktop action.",
      memory_query: message
    };
  }

  private sanitizePlan(plan: AgentPlan): AgentPlan {
    const merged = { ...EMPTY_PLAN, ...plan };
    if (!merged.action) return merged;
    const action = getActionDefinition(merged.action);
    if (!action) {
      return {
        ...EMPTY_PLAN,
        intent: "blocked_unknown_action",
        response: `I cannot run "${merged.action}" because it is not in the allowlisted action registry.`,
        action: null,
        safety_level: "blocked"
      };
    }
    return {
      ...merged,
      safety_level: action.safetyLevel,
      requires_confirmation: action.requiresConfirmation,
      confirmation_message: action.requiresConfirmation
        ? merged.confirmation_message || `Do you want me to run ${action.name}?`
        : ""
    };
  }

  private memoryTitle(message: string, type: string): string {
    const cleaned = message.replace(/\bremember\b/gi, "").trim();
    if (type === "link_memory") return "Saved link";
    return cleaned.slice(0, 80) || "Saved memory";
  }

  private tagsFromText(message: string): string[] {
    const lower = message.toLowerCase();
    return ["todo", "elastic", "hackathon", "deadline", "project"]
      .filter((tag) => lower.includes(tag))
      .slice(0, 6);
  }

  private shouldSaveChat(message: string, plan: AgentPlan, memories: MemoryDocument[]): boolean {
    return (
      message.length >= 32 ||
      memories.length > 0 ||
      plan.action !== null ||
      plan.intent !== "chat" ||
      /\b(project|deadline|decision|preference|remember|failed|action)\b/i.test(message)
    );
  }
}
