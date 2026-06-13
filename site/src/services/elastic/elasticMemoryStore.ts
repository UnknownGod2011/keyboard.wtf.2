import { Client } from "@elastic/elasticsearch";
import type { AppEnv } from "../../config/env.js";
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

type ElasticHit = {
  _id: string;
  _source?: MemoryDocument;
};

export class ElasticMemoryStore implements MemoryStore {
  private readonly client?: Client;

  constructor(private readonly env: AppEnv) {
    if (env.elasticsearchUrl && env.elasticsearchApiKey) {
      this.client = new Client({
        node: env.elasticsearchUrl,
        auth: { apiKey: env.elasticsearchApiKey }
      });
    }
  }

  async status() {
    if (!this.client) {
      return {
        configured: false,
        connected: false,
        message: "Elastic is not configured. Add ELASTICSEARCH_URL and ELASTICSEARCH_API_KEY server-side."
      };
    }

    try {
      await this.client.info();
      return { configured: true, connected: true, message: "Elastic connected." };
    } catch (error) {
      return {
        configured: true,
        connected: false,
        message: error instanceof Error ? error.message : "Elastic connection failed."
      };
    }
  }

  async ensureIndices(): Promise<void> {
    if (!this.client) return;
    await Promise.all([
      this.ensureIndex(this.env.elasticIndexMemories),
      this.ensureIndex(this.env.elasticIndexActions),
      this.ensureIndex(this.env.elasticIndexChats),
      this.ensureIndex(this.env.elasticIndexFailures)
    ]);
  }

  async saveMemory(user: UserContext, input: MemoryInput): Promise<MemoryDocument> {
    assertUserScoped(user);
    const doc = normalizeMemory(user, input);
    await this.indexDoc(this.env.elasticIndexMemories, doc);
    return doc;
  }

  async searchMemories(user: UserContext, options: MemorySearchOptions): Promise<MemoryDocument[]> {
    assertUserScoped(user);
    const hits = await this.searchIndex(this.env.elasticIndexMemories, user, options);
    return hits;
  }

  async listMemories(user: UserContext, options: MemorySearchOptions = {}): Promise<MemoryDocument[]> {
    return this.searchMemories(user, { ...options, sort: "date", query: options.query ?? "" });
  }

  async countMemories(user: UserContext): Promise<number> {
    assertUserScoped(user);
    if (!this.client) return 0;
    const response = await this.client.count({
      index: this.env.elasticIndexMemories,
      query: { term: { user_id: user.userId } }
    });
    return response.count;
  }

  async deleteMemory(user: UserContext, id: string): Promise<boolean> {
    assertUserScoped(user);
    if (!this.client || !id) return false;
    try {
      const existing = await this.client.get<MemoryDocument>({
        index: this.env.elasticIndexMemories,
        id
      });
      if (existing._source?.user_id !== user.userId) return false;
      await this.client.delete({
        index: this.env.elasticIndexMemories,
        id,
        refresh: true
      });
      return true;
    } catch {
      return false;
    }
  }

  async clearMemories(user: UserContext): Promise<number> {
    assertUserScoped(user);
    if (!this.client) return 0;
    const response = await this.client.deleteByQuery({
      index: this.env.elasticIndexMemories,
      refresh: true,
      query: { term: { user_id: user.userId } }
    });
    return response.deleted ?? 0;
  }

  async saveChat(user: UserContext, input: ChatLogInput): Promise<MemoryDocument> {
    assertUserScoped(user);
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
    await this.indexDoc(this.env.elasticIndexChats, doc);
    await this.indexDoc(this.env.elasticIndexMemories, doc);
    return doc;
  }

  async saveAction(user: UserContext, input: ActionLogInput): Promise<MemoryDocument> {
    assertUserScoped(user);
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
    await this.indexDoc(this.env.elasticIndexActions, doc);
    await this.indexDoc(this.env.elasticIndexMemories, doc);
    return doc;
  }

  async saveFailure(user: UserContext, input: Omit<ActionLogInput, "status">): Promise<MemoryDocument> {
    assertUserScoped(user);
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
    await this.indexDoc(this.env.elasticIndexFailures, doc);
    await this.indexDoc(this.env.elasticIndexMemories, doc);
    return doc;
  }

  async listActions(user: UserContext, limit = 20): Promise<MemoryDocument[]> {
    return this.searchIndex(this.env.elasticIndexActions, user, { limit, sort: "date" });
  }

  async listFailures(user: UserContext, limit = 20): Promise<MemoryDocument[]> {
    return this.searchIndex(this.env.elasticIndexFailures, user, { limit, sort: "date" });
  }

  async searchActions(user: UserContext, query: string, limit = 10): Promise<MemoryDocument[]> {
    return this.searchIndex(this.env.elasticIndexActions, user, { query, limit });
  }

  private async ensureIndex(index: string): Promise<void> {
    if (!this.client) return;
    const exists = await this.client.indices.exists({ index });
    if (exists) return;

    await this.client.indices.create({
      index,
      mappings: {
        properties: {
          id: { type: "keyword" },
          user_id: { type: "keyword" },
          device_id: { type: "keyword" },
          type: { type: "keyword" },
          title: { type: "text" },
          content: { type: "text" },
          summary: { type: "text" },
          source_app: { type: "keyword" },
          source_window: { type: "keyword" },
          url: { type: "keyword" },
          tags: { type: "keyword" },
          importance: { type: "float" },
          privacy_level: { type: "keyword" },
          action_status: { type: "keyword" },
          created_at: { type: "date" },
          updated_at: { type: "date" },
          last_accessed_at: { type: "date" },
          metadata: { type: "object", enabled: true }
        }
      }
    });
  }

  private async indexDoc(index: string, doc: MemoryDocument): Promise<void> {
    if (!this.client) {
      throw new Error("Elastic is not configured. Server-side Elastic credentials are required.");
    }
    await this.client.index({
      index,
      id: doc.id,
      refresh: true,
      document: doc
    });
  }

  private async searchIndex(
    index: string,
    user: UserContext,
    options: MemorySearchOptions = {}
  ): Promise<MemoryDocument[]> {
    assertUserScoped(user);
    if (!this.client) return [];

    const filters: Record<string, unknown>[] = [{ term: { user_id: user.userId } }];
    if (options.types?.length) filters.push({ terms: { type: options.types } });
    if (options.tags?.length) filters.push({ terms: { tags: options.tags } });
    if (!options.includeSensitive) filters.push({ bool: { must_not: { term: { privacy_level: "sensitive" } } } });
    if (options.dateFrom || options.dateTo) {
      filters.push({
        range: {
          created_at: {
            ...(options.dateFrom ? { gte: options.dateFrom } : {}),
            ...(options.dateTo ? { lte: options.dateTo } : {})
          }
        }
      });
    }

    const queryText = options.query?.trim();
    const query = queryText
      ? {
          bool: {
            filter: filters,
            must: [
              {
                multi_match: {
                  query: queryText,
                  fields: ["title^3", "summary^2", "content", "tags^2", "url"],
                  fuzziness: "AUTO"
                }
              }
            ]
          }
        }
      : {
          bool: {
            filter: filters
          }
        };

    const response = await this.client.search<MemoryDocument>({
      index,
      size: Math.max(1, Math.min(options.limit ?? 20, 100)),
      query,
      sort: options.sort === "date" || !queryText ? [{ created_at: "desc" }] : undefined
    });

    return (response.hits.hits as ElasticHit[])
      .map((hit) => hit._source && { ...hit._source, id: hit._source.id || hit._id })
      .filter((doc): doc is MemoryDocument => Boolean(doc));
  }
}
