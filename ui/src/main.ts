import { mount } from "svelte";
import App from "./App.svelte";
import "./app.css";

if (new URLSearchParams(window.location.search).has("overlay")) {
  const Overlay = (await import("./Overlay.svelte")).default;
  mount(Overlay, { target: document.getElementById("app")! });
} else {
  mount(App, { target: document.getElementById("app")! });
}
