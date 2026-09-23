<script lang="ts">
  import { onMount } from "svelte";
  import { invoke } from "@tauri-apps/api/core";
  import { Bell, Keyboard, Volume2 } from "@lucide/svelte";

  type Preferences = { popups: boolean; sound: boolean; shortcuts: boolean };
  let preferences = $state<Preferences | null>(null);
  let busy = $state(false);
  let error = $state("");
  let previewMessage = $state("");
  onMount(() => {
    void invoke<Preferences>("notification_settings")
      .then(value => preferences = value).catch(reason => error = String(reason));
  });

  async function change(key: keyof Preferences, value: boolean) {
    if (!preferences) return;
    busy = true;
    error = "";
    const previous = preferences;
    const next = { ...preferences, [key]: value };
    preferences = next;
    try {
      await invoke("save_notification_settings", { settings: next });
      preferences = next;
    } catch (reason) { preferences = previous; error = String(reason); }
    finally { busy = false; }
  }

  async function preview(activity: string) {
    error = "";
    try {
      await invoke("preview_notification", { activity });
      previewMessage = "Preview sent to your primary display.";
    } catch (reason) { error = String(reason); }
  }
</script>

<section class="settings-group notification-settings" aria-labelledby="notification-heading">
  <header class="settings-group__header"><div><p>Desktop alerts</p><h3 id="notification-heading">Readiness notifications</h3></div><Bell size={17} /></header>
  <p class="notification-copy">A brief alert when the pickpocket cooldown ends, a new fishing cast starts, or your pickpocket shortcut starts and stops a run. Changes save immediately.</p>
  <label><span><Bell size={14} /> Top-right popups</span><input type="checkbox" checked={preferences?.popups ?? false} disabled={!preferences || busy} onchange={event => void change("popups", event.currentTarget.checked)} /></label>
  <label><span><Volume2 size={14} /> Alert sound</span><input type="checkbox" checked={preferences?.sound ?? false} disabled={!preferences || busy} onchange={event => void change("sound", event.currentTarget.checked)} /></label>
  <label><span><Keyboard size={14} /> Shortcut confirmations</span><input type="checkbox" checked={preferences?.shortcuts ?? false} disabled={!preferences || busy} onchange={event => void change("shortcuts", event.currentTarget.checked)} /></label>
  <p class="notification-copy">Click-through, without taking focus. Sound follows your Windows sound scheme and volume.</p>
  <div class="notification-previews">
    <button class="sub-action" disabled={!preferences || busy || (!preferences.popups && !preferences.sound)} onclick={() => void preview("pickpocket")}>Preview pickpocket</button>
    <button class="sub-action" disabled={!preferences || busy || (!preferences.popups && !preferences.sound)} onclick={() => void preview("fishing")}>Preview fishing</button>
    <button class="sub-action" disabled={!preferences || busy || (!preferences.popups && !preferences.sound)} onclick={() => void preview("shortcut")}>Preview shortcut</button>
  </div>
  {#if previewMessage}<p class="notification-copy" role="status">{previewMessage}</p>{/if}
  {#if error}<p class="error" role="alert">Notifications: {error}</p>{/if}
</section>

<style>
  .notification-copy { color: var(--text-muted); font-size: 11px; line-height: 1.6; margin: 10px 0; }
  label { display: flex; justify-content: space-between; align-items: center; padding: 8px 0; font-size: 12px; }
  label span { display: inline-flex; align-items: center; gap: 8px; }
  input { width: 16px; height: 16px; accent-color: #d97757; }
  .notification-previews { display: flex; flex-wrap: wrap; gap: 8px; }
</style>
