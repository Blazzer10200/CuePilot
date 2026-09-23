import { mount } from "svelte";
import App from "./App.svelte";
import "./app.css";
import { isTauri } from "@tauri-apps/api/core";
import { getCurrentWindow } from "@tauri-apps/api/window";

if (import.meta.env.DEV && new URLSearchParams(location.search).has("scenario")) {
  const { installScenario } = await import("./dev/scenarios");
  installScenario(new URLSearchParams(location.search).get("scenario") || "history");
}

if (isTauri() && getCurrentWindow().label === "notification") {
  const Overlay = (await import("./Overlay.svelte")).default;
  mount(Overlay, { target: document.getElementById("app")! });
} else {
  mount(App, { target: document.getElementById("app")! });
}
