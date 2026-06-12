import { describe, expect, it } from "vitest";
import { InMemoryMemoryStore } from "../src/services/elastic/inMemoryMemoryStore.js";

const user = { userId: "tanushshah2006", deviceId: "tanush-windows-demo" };

describe("elastic memory store contract", () => {
  it("saves, lists, searches, deletes, and clears memories for a scoped user", async () => {
    const store = new InMemoryMemoryStore();
    const saved = await store.saveMemory(user, {
      type: "link_memory",
      title: "Hackathon deadline",
      content: "Remember the Google Cloud Rapid Agent Hackathon deadline.",
      tags: ["hackathon"],
      url: "https://rapid-agent.devpost.com/"
    });

    expect(saved.user_id).toBe(user.userId);
    expect(saved.device_id).toBe(user.deviceId);

    expect(await store.countMemories(user)).toBe(1);
    expect((await store.listMemories(user))[0].id).toBe(saved.id);
    expect((await store.searchMemories(user, { query: "deadline", limit: 5 }))[0].id).toBe(saved.id);

    expect(await store.deleteMemory(user, saved.id)).toBe(true);
    expect(await store.countMemories(user)).toBe(0);

    await store.saveMemory(user, { content: "one" });
    await store.saveMemory(user, { content: "two" });
    expect(await store.clearMemories(user)).toBe(2);
  });

  it("logs actions and failures as scoped memory documents", async () => {
    const store = new InMemoryMemoryStore();
    const action = await store.saveAction(user, {
      action: "open_url",
      detail: "Opened example.com",
      status: "completed",
      safety_level: "safe"
    });
    const failure = await store.saveFailure(user, {
      action: "chrome_close_tab",
      detail: "Bridge offline",
      safety_level: "confirm",
      requires_confirmation: true
    });

    expect(action.type).toBe("action_memory");
    expect(failure.type).toBe("failure_memory");
    expect(await store.listActions(user)).toHaveLength(1);
    expect(await store.listFailures(user)).toHaveLength(1);
    expect(await store.searchActions(user, "example")).toHaveLength(1);
  });
});
