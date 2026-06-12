import type { UserContext } from "../users/userContext.js";
import { assertUserScoped } from "../users/userContext.js";
import {
  type ActionLogInput,
  type ChatLogInput,
  type MemoryDocument,
  type MemoryInput,
  type MemorySearchOptions,
  type MemoryStore,
  normalizeMemory
} from "./types.js";

export class InMemoryMemoryStore implements MemoryStore {
  private readonly memories = new Map<string, MemoryDocument>();
  private readonly actions = new Map<string, MemoryDocument>();
  private readonly chats = new Map<string, MemoryDocument>();
  private readonly failures = new Map<string, MemoryDocument>();

  async status() {
    return { configured: false, connected: false, message: "Elastic is not configured in test memory store." };
  }

  async ensureIndices() {
    return undefined;
  }

  async saveMemory(user: UserContext, input: MemoryInput): Promise<MemoryDocument> {
    assertUserScoped(user);
    const doc = normalizeMemory(user, input);
    this.memories.set(doc.id, doc);
    return doc;
  }

  async searchMemories(user: UserContext, options: MemorySearchOptions): Promise<MemoryDocument[]> {
    assertUserScoped(user);
    return this.filter([...this.memories.values()], user, options);
  }

  async listMemories(user: UserContext, options: MemorySearchOptions = {}): Promise<MemoryDocument[]> {
    return this.searchMemories(user, { ...options, sort: "date" });
  }

  async countMemories(user: UserContext): Promise<number> {
    assertUserScoped(user);
    return [...this.memories.values()].filter((doc) => doc.user_id === user.userId).length;
  }

  async deleteMemory(user: UserContext, id: string): Promise<boolean> {
    assertUserScoped(user);
    const doc = this.memories.get(id);
    if (!doc || doc.user_id !== user.userId) return false;
    return this.memories.delete(id);
  }

  async clearMemories(user: UserContext): Promise<number> {
    assertUserScoped(user);
    let removed = 0;
    for (const [id, doc] of this.memories.entries()) {
      if (doc.user_id === user.userId) {
        this.memories.delete(id);
        removed++;
      }
    }
    return removed;
  }

  async saveChat(user: UserContext, input: ChatLogInput): Promise<MemoryDocument> {
    const doc = normalizeMemory(user, {
      type: "chat_memory",
      title: `${input.role}: ${input.content.slice(0, 64)}`,
      content: input.content,
      summary: input.summary ?? input.content.slice(0, 240),
      metadata: {
        role: input.role,
        memory_used_count: input.memory_used_count ?? 0,
        ...input.metadata
      }
    });
    this.chats.set(doc.id, doc);
    this.memories.set(doc.id, doc);
    return doc;
  }

  async saveAction(user: UserContext, input: ActionLogInput): Promise<MemoryDocument> {
    const doc = normalizeMemory(user, {
      type: "action_memory",
      title: input.action,
      content: input.detail ?? input.action,
      summary: `${input.status}: ${input.detail ?? input.action}`,
      action_status: input.status,
      metadata: {
        safety_level: input.safety_level,
        requires_confirmation: Boolean(input.requires_confirmation),
        ...input.metadata
      }
    });
    this.actions.set(doc.id, doc);
    this.memories.set(doc.id, doc);
    return doc;
  }

  async saveFailure(user: UserContext, input: Omit<ActionLogInput, "status">): Promise<MemoryDocument> {
    const doc = normalizeMemory(user, {
      type: "failure_memory",
      title: input.action,
      content: input.detail ?? input.action,
      summary: `failed: ${input.detail ?? input.action}`,
      action_status: "failed",
      metadata: {
        safety_level: input.safety_level,
        requires_confirmation: Boolean(input.requires_confirmation),
        ...input.metadata
      }
    });
    this.failures.set(doc.id, doc);
    this.memories.set(doc.id, doc);
    return doc;
  }

  async listActions(user: UserContext, limit = 20): Promise<MemoryDocument[]> {
    return this.filter([...this.actions.values()], user, { limit, sort: "date" });
  }

  async listFailures(user: UserContext, limit = 20): Promise<MemoryDocument[]> {
    return this.filter([...this.failures.values()], user, { limit, sort: "date" });
  }

  async searchActions(user: UserContext, query: string, limit = 10): Promise<MemoryDocument[]> {
    return this.filter([...this.actions.values()], user, { query, limit });
  }

  private filter(
    docs: MemoryDocument[],
    user: UserContext,
    options: MemorySearchOptions = {}
  ): MemoryDocument[] {
    const query = options.query?.trim().toLowerCase();
    const tags = options.tags ?? [];
    const types = options.types ?? [];
    const limit = Math.max(1, Math.min(options.limit ?? 20, 100));

    return docs
      .filter((doc) => doc.user_id === user.userId)
      .filter((doc) => (types.length ? types.includes(doc.type) : true))
      .filter((doc) => (tags.length ? tags.every((tag) => doc.tags.includes(tag)) : true))
      .filter((doc) => (options.includeSensitive ? true : doc.privacy_level !== "sensitive"))
      .filter((doc) => {
        if (!query) return true;
        const haystack = `${doc.title} ${doc.content} ${doc.summary} ${doc.tags.join(" ")}`.toLowerCase();
        return haystack.includes(query);
      })
      .sort((a, b) => {
        if (options.sort === "date") {
          return Date.parse(b.created_at) - Date.parse(a.created_at);
        }
        return Date.parse(b.updated_at) - Date.parse(a.updated_at);
      })
      .slice(0, limit);
  }
}
