import { defineConfig } from "vite";
import { svelte } from "@sveltejs/vite-plugin-svelte";
import tauriConfig from "./src-tauri/tauri.conf.json";

export default defineConfig({
  plugins: [svelte()],
  clearScreen: false,
  server: { port: 1420, strictPort: true },
  envPrefix: ["VITE_", "TAURI_"],
  define: { __APP_VERSION__: JSON.stringify(tauriConfig.version) },
  build: { target: "es2022", minify: !process.env.TAURI_ENV_DEBUG },
});
