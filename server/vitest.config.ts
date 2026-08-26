import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";
import { loadTestEnvironment } from "./tests/load-test-environment.ts";

loadTestEnvironment();

export default defineConfig({
  resolve: {
    alias: {
      "@": fileURLToPath(new URL(".", import.meta.url)),
    },
  },
  test: {
    environment: "node",
    include: ["tests/**/*.test.ts"],
    fileParallelism: false,
    testTimeout: 15_000,
    coverage: {
      reporter: ["text", "html"],
    },
  },
});
