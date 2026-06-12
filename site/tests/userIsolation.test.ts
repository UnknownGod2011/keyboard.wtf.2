import { describe, expect, it } from "vitest";
import { InMemoryMemoryStore } from "../src/services/elastic/inMemoryMemoryStore.js";

const userA = { userId: "user-a", deviceId: "device-a" };
const userB = { userId: "user-b", deviceId: "device-b" };

describe("user isolation", () => {
  it("prevents user A from seeing, deleting, or clearing user B memory", async () => {
    const store = new InMemoryMemoryStore();
    const a = await store.saveMemory(userA, {
      title: "A todo link",
      content: "Remember https://a.example/todo",
      url: "https://a.example/todo",
      type: "link_memory"
    });
    const b = await store.saveMemory(userB, {
      title: "B secret link",
      content: "Remember https://b.example/private",
      url: "https://b.example/private",
      type: "link_memory"
    });

    expect(await store.countMemories(userA)).toBe(1);
    expect(await store.countMemories(userB)).toBe(1);

    const aSearch = await store.searchMemories(userA, { query: "link", limit: 10 });
    expect(aSearch.map((memory) => memory.id)).toEqual([a.id]);

    expect(await store.deleteMemory(userA, b.id)).toBe(false);
    expect(await store.countMemories(userB)).toBe(1);

    expect(await store.clearMemories(userA)).toBe(1);
    expect(await store.countMemories(userA)).toBe(0);
    expect(await store.countMemories(userB)).toBe(1);
    expect((await store.listMemories(userB))[0].id).toBe(b.id);
  });

  it("requires a user_id for scoped operations", async () => {
    const store = new InMemoryMemoryStore();
    await expect(
      store.saveMemory({ userId: "", deviceId: "device" }, { content: "no owner" })
    ).rejects.toThrow(/user_id/);
  });
});
