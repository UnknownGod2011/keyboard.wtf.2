import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

const dashboardPath = new URL("../public/index.html", import.meta.url);

describe("dashboard voice Jarvis", () => {
  it("provides a triggered voice conversation with an explicit stop", async () => {
    const html = await readFile(dashboardPath, "utf8");

    expect(html).toContain('id="voiceLauncher"');
    expect(html).toContain('id="openVoiceInline"');
    expect(html).toContain('id="voiceStart"');
    expect(html).toContain('id="voiceStop"');
    expect(html).toContain("speechRecognitionConstructor");
    expect(html).toContain("speechSynthesis");
    expect(html).toContain("voiceConversationActive = false");
    expect(html).toContain('document.addEventListener("visibilitychange"');
    expect(html).toContain("voicePausedForVisibility");
    expect(html).toContain("voicePendingReply");
    expect(html).toContain("Return to this dashboard tab and Jarvis will continue");
  });

  it("keeps the bridge token out of ordinary Cloud Run API headers", async () => {
    const html = await readFile(dashboardPath, "utf8");
    const headersFunction = html.match(/function headers\(\) \{[\s\S]*?\n    \}/)?.[0] ?? "";

    expect(headersFunction).toContain("x-keyboard-user-id");
    expect(headersFunction).toContain("x-keyboard-device-id");
    expect(headersFunction).not.toContain("bridgeToken");
    expect(html).toContain('fetch("http://localhost:8787/api/bridge/action"');
    expect(html).toContain("Authorization: `Bearer ${state.bridgeToken}`");
  });
});
