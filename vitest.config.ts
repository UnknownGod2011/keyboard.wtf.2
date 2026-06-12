import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    include: ["site/tests/**/*.test.ts"],
    environment: "node",
    globals: true
  }
});
