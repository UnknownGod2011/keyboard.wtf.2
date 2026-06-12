import { describe, expect, it } from "vitest";
import { ACTION_REGISTRY, assertAllowedAction, getActionDefinition } from "../src/services/actions/actionRegistry.js";

describe("action registry", () => {
  it("contains only allowlisted actions with explicit safety metadata", () => {
    expect(ACTION_REGISTRY.length).toBeGreaterThan(5);
    for (const action of ACTION_REGISTRY) {
      expect(action.name).toMatch(/^[a-z0-9_]+$/);
      expect(["safe", "confirm", "blocked"]).toContain(action.safetyLevel);
      expect(typeof action.requiresConfirmation).toBe("boolean");
      expect(action.platformSupport).toBe("windows_bridge");
    }
  });

  it("requires confirmation for risky tab close and email drafts", () => {
    expect(getActionDefinition("chrome_close_tab")?.requiresConfirmation).toBe(true);
    expect(getActionDefinition("draft_email")?.safetyLevel).toBe("confirm");
  });

  it("rejects arbitrary model-selected actions", () => {
    expect(() => assertAllowedAction("run_shell_command")).toThrow(/allowlisted/);
  });
});
