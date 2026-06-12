import type { AppEnv } from "../../config/env.js";
import { assertAllowedAction } from "../actions/actionRegistry.js";
import type { UserContext } from "../users/userContext.js";

export type BridgeCommand = {
  action: string;
  params?: Record<string, unknown>;
  confirmed?: boolean;
  pairingToken?: string;
};

export type BridgeResult = {
  ok: boolean;
  bridgeOnline: boolean;
  demoMode: boolean;
  message: string;
  data?: unknown;
};

export class LocalBridgeClient {
  constructor(private readonly env: AppEnv) {}

  get baseUrl(): string {
    return `http://localhost:${this.env.localBridgePort}`;
  }

  async status(pairingToken?: string): Promise<BridgeResult> {
    try {
      const response = await fetch(`${this.baseUrl}/api/bridge/status`, {
        headers: this.headers(pairingToken),
        signal: AbortSignal.timeout(1200)
      });
      const data = await response.json().catch(() => ({}));
      return {
        ok: response.ok,
        bridgeOnline: response.ok,
        demoMode: false,
        message: response.ok ? "Desktop bridge online." : data.error ?? "Desktop bridge rejected status check.",
        data
      };
    } catch {
      return {
        ok: false,
        bridgeOnline: false,
        demoMode: true,
        message: "Desktop bridge offline. Cloud dashboard remains in safe demo mode."
      };
    }
  }

  async sendCommand(user: UserContext, command: BridgeCommand): Promise<BridgeResult> {
    const action = assertAllowedAction(command.action);
    if (action.requiresConfirmation && !command.confirmed) {
      return {
        ok: false,
        bridgeOnline: false,
        demoMode: false,
        message: `${action.name} requires explicit confirmation before it can run.`
      };
    }

    const bridge = await this.status(command.pairingToken);
    if (!bridge.bridgeOnline) return bridge;

    try {
      const response = await fetch(`${this.baseUrl}/api/bridge/action`, {
        method: "POST",
        headers: {
          ...this.headers(command.pairingToken),
          "content-type": "application/json"
        },
        body: JSON.stringify({
          user_id: user.userId,
          device_id: user.deviceId,
          action: command.action,
          params: command.params ?? {},
          safety_level: action.safetyLevel,
          requires_confirmation: action.requiresConfirmation,
          confirmed: Boolean(command.confirmed)
        }),
        signal: AbortSignal.timeout(5000)
      });
      const data = await response.json().catch(() => ({}));
      return {
        ok: response.ok && data.ok !== false,
        bridgeOnline: true,
        demoMode: false,
        message: data.message ?? (response.ok ? "Bridge action completed." : "Bridge action failed."),
        data
      };
    } catch (error) {
      return {
        ok: false,
        bridgeOnline: false,
        demoMode: true,
        message: error instanceof Error ? error.message : "Desktop bridge action failed."
      };
    }
  }

  private headers(pairingToken?: string): Record<string, string> {
    return pairingToken ? { authorization: `Bearer ${pairingToken}` } : {};
  }
}
