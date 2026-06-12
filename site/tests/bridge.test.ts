import { afterEach, describe, expect, it, vi } from "vitest";
import { loadEnv } from "../src/config/env.js";
import { LocalBridgeClient } from "../src/services/bridge/localBridgeClient.js";

describe("local bridge client", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("reports safe demo mode when the desktop bridge is offline", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("offline")));
    const bridge = new LocalBridgeClient(loadEnv({ LOCAL_BRIDGE_PORT: "8787" }));

    const status = await bridge.status();

    expect(status.ok).toBe(false);
    expect(status.bridgeOnline).toBe(false);
    expect(status.demoMode).toBe(true);
    expect(status.message).toMatch(/offline/i);
  });

  it("does not send confirmation-required actions without confirmation", async () => {
    const fetchSpy = vi.fn();
    vi.stubGlobal("fetch", fetchSpy);
    const bridge = new LocalBridgeClient(loadEnv({ LOCAL_BRIDGE_PORT: "8787" }));

    const result = await bridge.sendCommand(
      { userId: "u", deviceId: "d" },
      { action: "chrome_close_tab", confirmed: false }
    );

    expect(result.ok).toBe(false);
    expect(result.message).toMatch(/requires explicit confirmation/i);
    expect(fetchSpy).not.toHaveBeenCalled();
  });
});
