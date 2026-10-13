import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { defineConfig, type Plugin } from "vite";
import react from "@vitejs/plugin-react";

function isolatedPreviewApi(): Plugin {
  const previewRoot = resolve(__dirname, "..", "preview-data");
  const readJson = (name: string, fallback: unknown) => {
    const path = resolve(previewRoot, name);
    if (!existsSync(path)) return fallback;
    try { return JSON.parse(readFileSync(path, "utf8")); } catch { return fallback; }
  };
  const readText = (name: string) => { const path = resolve(previewRoot, name); return existsSync(path) ? readFileSync(path, "utf8") : ""; };
  return {
    name: "game-management-isolated-preview",
    configurePreviewServer(server) {
      server.middlewares.use((request, response, next) => {
        if (request.url !== "/api/snapshot") {
          if (request.url?.startsWith("/api/") && request.method !== "GET") { response.statusCode = 403; response.end("Live commands are disabled in isolated preview mode."); return; }
          next(); return;
        }
        response.setHeader("Content-Type", "application/json; charset=utf-8"); response.setHeader("Cache-Control", "no-store");
        const taskDocument = readJson("tasks.json", { Items: [] }) as { Items?: unknown[] };
        response.end(JSON.stringify({
          serverUtc: new Date().toISOString(), rootPath: previewRoot, watcherRunning: false, engineEnabled: false,
          settings: readJson("settings.json", {}), runtime: readJson("runtime-state.json", {}),
          timers: { work: readJson("work-timer-state.json", null), game: readJson("timer-state.json", null) },
          sessions: { work: readJson("work-session-state.json", null), game: readJson("game-session-state.json", null) },
          counters: readJson("timer-counters.json", {}), performance: readJson("performance-state.json", null),
          tasks: taskDocument.Items || [], history: readText("GameSessionHistory.txt"),
          panelPreferences: readJson("panel-preferences.json", { Version: 1, CalendarPinned: false, TasksPinned: false })
        }));
      });
    }
  };
}

export default defineConfig({
  plugins: [react(), isolatedPreviewApi()],
  base: "./",
  server: { port: 5173, strictPort: true },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      output: {
        entryFileNames: "assets/app.js",
        chunkFileNames: "assets/[name].js",
        assetFileNames: (assetInfo) =>
          assetInfo.name?.endsWith(".css")
            ? "assets/app.css"
            : "assets/[name][extname]"
      }
    }
  }
});
