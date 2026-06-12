import type { Request } from "express";
import type { AppEnv } from "../../config/env.js";

export type UserContext = {
  userId: string;
  deviceId: string;
};

function clean(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;
  const trimmed = value.trim();
  if (!trimmed) return undefined;
  return trimmed.replace(/[^\w.@-]/g, "").slice(0, 96);
}

export function userContextFromRequest(req: Request, env: AppEnv): UserContext {
  return {
    userId:
      clean(req.header("x-keyboard-user-id")) ??
      clean(req.query.user_id) ??
      clean(req.body?.user_id) ??
      env.defaultUserId,
    deviceId:
      clean(req.header("x-keyboard-device-id")) ??
      clean(req.query.device_id) ??
      clean(req.body?.device_id) ??
      env.defaultDeviceId
  };
}

export function assertUserScoped(user: UserContext): void {
  if (!user.userId) {
    throw new Error("A user_id is required for every memory, chat, action, and failure operation.");
  }
}
