import type { UserContext } from "../users/userContext.js";

export const MEMORY_TYPES = [
  "semantic_memory",
  "episodic_memory",
  "action_memory",
  "document_memory",
  "chat_memory",
  "failure_memory",
  "preference_memory",
  "link_memory"
] as const;

export type MemoryType = (typeof MEMORY_TYPES)[number];
export type PrivacyLevel = "normal" | "private" | "sensitive";
export type ActionSafetyLevel = "safe" | "confirm" | "blocked";

export type MemoryDocument = {
  id: string;
  user_id: string;
  device_id: string;
  type: MemoryType;
  title: string;
  content: string;
  summary: string;
  source_app: string;
  source_window: string;
  url: string;
  tags: string[];
  importance: number;
  privacy_level: PrivacyLevel;
  action_status: string;
  created_at: string;
  updated_at: string;
  last_accessed_at: string;
  metadata: Record<string, unknown>;
};

export type MemoryInput = Partial<Omit<MemoryDocument, "id" | "user_id" | "device_id">> & {
  id?: string;
  type?: MemoryType;
  title?: string;
  content: string;
};

export type MemorySearchOptions = {
  query?: string;
  types?: MemoryType[];
  tags?: string[];
  dateFrom?: string;
  dateTo?: string;
  limit?: number;
  includeSensitive?: boolean;
  sort?: "relevance" | "date";
};

export type ActionLogInput = {
  action: string;
  detail?: string;
  status: "completed" | "failed" | "confirmation_required" | "demo_mode" | "blocked";
  safety_level: ActionSafetyLevel;
  requires_confirmation?: boolean;
  metadata?: Record<string, unknown>;
};

export type ChatLogInput = {
  role: "user" | "assistant" | "system";
  content: string;
  summary?: string;
  memory_used_count?: number;
  metadata?: Record<string, unknown>;
};

export type MemoryStoreStatus = {
  configured: boolean;
  connected: boolean;
  message: string;
};

export interface MemoryStore {
  status(): Promise<MemoryStoreStatus>;
  ensureIndices(): Promise<void>;
  saveMemory(user: UserContext, input: MemoryInput): Promise<MemoryDocument>;
  searchMemories(user: UserContext, options: MemorySearchOptions): Promise<MemoryDocument[]>;
  listMemories(user: UserContext, options?: MemorySearchOptions): Promise<MemoryDocument[]>;
  countMemories(user: UserContext): Promise<number>;
  deleteMemory(user: UserContext, id: string): Promise<boolean>;
  clearMemories(user: UserContext): Promise<number>;
  saveChat(user: UserContext, input: ChatLogInput): Promise<MemoryDocument>;
  saveAction(user: UserContext, input: ActionLogInput): Promise<MemoryDocument>;
  saveFailure(user: UserContext, input: Omit<ActionLogInput, "status">): Promise<MemoryDocument>;
  listActions(user: UserContext, limit?: number): Promise<MemoryDocument[]>;
  listFailures(user: UserContext, limit?: number): Promise<MemoryDocument[]>;
  searchActions(user: UserContext, query: string, limit?: number): Promise<MemoryDocument[]>;
}

export function normalizeMemory(user: UserContext, input: MemoryInput): MemoryDocument {
  const now = new Date().toISOString();
  const id = input.id ?? crypto.randomUUID();
  return {
    id,
    user_id: user.userId,
    device_id: user.deviceId,
    type: input.type ?? "semantic_memory",
    title: input.title?.trim() || input.content.slice(0, 80),
    content: input.content.trim(),
    summary: input.summary?.trim() ?? input.content.trim().slice(0, 240),
    source_app: input.source_app?.trim() ?? "",
    source_window: input.source_window?.trim() ?? "",
    url: input.url?.trim() ?? "",
    tags: Array.isArray(input.tags) ? input.tags.map((tag) => tag.trim()).filter(Boolean) : [],
    importance: Math.max(0, Math.min(10, Number(input.importance ?? 5))),
    privacy_level: input.privacy_level ?? "normal",
    action_status: input.action_status?.trim() ?? "",
    created_at: input.created_at ?? now,
    updated_at: now,
    last_accessed_at: input.last_accessed_at ?? now,
    metadata: input.metadata ?? {}
  };
}

export function isMemoryType(value: string): value is MemoryType {
  return (MEMORY_TYPES as readonly string[]).includes(value);
}
