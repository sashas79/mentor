/// <reference types="vitest/config" />
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import { defineConfig, type Plugin } from "vite";

const web = dirname(fileURLToPath(import.meta.url));
const tests = resolve(web, "../../tests/web");

/**
 * The tests live in tests/web, beside the backend's tests, while the packages they use are installed here. A package a
 * test imports is resolved as if a source file of this application imported it.
 */
function packagesForTests(): Plugin {
  return {
    name: "mentor-web:packages-for-tests",
    enforce: "pre",
    async resolveId(source, importer, options) {
      const bare = !source.startsWith(".") && !source.startsWith("/") && !source.startsWith("\0") && !source.startsWith("@/");
      if (!bare || !importer?.startsWith(tests)) {
        return null;
      }

      return this.resolve(source, join(web, "src", "main.tsx"), { ...options, skipSelf: true });
    },
  };
}

export default defineConfig({
  plugins: [react(), packagesForTests()],
  resolve: {
    alias: { "@": resolve(web, "src") },
  },
  server: {
    // In development the API is reached through this server, so no CORS origin is needed.
    proxy: { "/api": process.env.MENTOR_API_URL ?? "http://localhost:5080" },
    fs: { allow: [web, tests] },
  },
  build: {
    sourcemap: true,
  },
  test: {
    dir: tests,
    include: ["**/*.test.{ts,tsx}"],
    environment: "jsdom",
    setupFiles: [join(tests, "setup.ts")],
    restoreMocks: true,
    unstubGlobals: true,
  },
});
