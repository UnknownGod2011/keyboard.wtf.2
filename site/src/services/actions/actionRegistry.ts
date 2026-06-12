import type { ActionSafetyLevel } from "../elastic/types.js";

export type ActionDefinition = {
  name: string;
  description: string;
  parameters: Record<string, string>;
  safetyLevel: ActionSafetyLevel;
  requiresConfirmation: boolean;
  platformSupport: "windows_bridge" | "cloud_demo";
};

export const ACTION_REGISTRY: ActionDefinition[] = [
  {
    name: "open_url",
    description: "Open an http or https URL in the user's default browser.",
    parameters: { url: "Absolute http or https URL." },
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "open_app",
    description: "Open a known installed app such as File Explorer, Settings, Spotify, VS Code, or Chrome.",
    parameters: { app_name: "Known app display name." },
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "chrome_next_tab",
    description: "Move to the next foreground Chrome/browser tab.",
    parameters: {},
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "chrome_previous_tab",
    description: "Move to the previous foreground Chrome/browser tab.",
    parameters: {},
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "chrome_new_tab",
    description: "Open a new foreground browser tab.",
    parameters: {},
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "chrome_reopen_tab",
    description: "Reopen the last closed browser tab.",
    parameters: {},
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "chrome_close_tab",
    description: "Close the current foreground browser tab.",
    parameters: {},
    safetyLevel: "confirm",
    requiresConfirmation: true,
    platformSupport: "windows_bridge"
  },
  {
    name: "search_google",
    description: "Open a Google search for the provided query.",
    parameters: { query: "Search query." },
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "search_youtube",
    description: "Open a YouTube search for the provided query.",
    parameters: { query: "Search query." },
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "open_gmail_compose",
    description: "Open Gmail compose with optional recipient, subject, and body. Never sends.",
    parameters: { recipient: "Email recipient.", subject: "Draft subject.", body: "Draft body." },
    safetyLevel: "confirm",
    requiresConfirmation: true,
    platformSupport: "windows_bridge"
  },
  {
    name: "draft_email",
    description: "Prepare an email draft for user review. Never sends.",
    parameters: { recipient: "Email recipient.", subject: "Draft subject.", body: "Draft body." },
    safetyLevel: "confirm",
    requiresConfirmation: true,
    platformSupport: "windows_bridge"
  },
  {
    name: "copy_to_clipboard",
    description: "Copy text to the local clipboard.",
    parameters: { text: "Text to copy." },
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "switch_window",
    description: "Switch to an open window by title or app name.",
    parameters: { app_name: "Window title or app name." },
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  },
  {
    name: "show_desktop",
    description: "Show the Windows desktop.",
    parameters: {},
    safetyLevel: "safe",
    requiresConfirmation: false,
    platformSupport: "windows_bridge"
  }
];

export function getActionDefinition(name: string): ActionDefinition | undefined {
  return ACTION_REGISTRY.find((action) => action.name === name);
}

export function assertAllowedAction(name: string): ActionDefinition {
  const definition = getActionDefinition(name);
  if (!definition) {
    throw new Error(`${name} is not in the keyboard.wtf allowlisted action registry.`);
  }
  return definition;
}

export function registryForPrompt(): string {
  return ACTION_REGISTRY.map((action) => {
    const params = Object.keys(action.parameters).join(", ") || "none";
    return `- ${action.name}: ${action.description} Parameters: ${params}. Safety: ${action.safetyLevel}.`;
  }).join("\n");
}
