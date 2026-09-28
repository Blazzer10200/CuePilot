<script lang="ts">
  import { invoke } from "@tauri-apps/api/core";
  import { Keyboard, Mouse, RotateCcw } from "@lucide/svelte";
  import { phaseLock } from "./motion";
  import {
    bindingFromKeyboardEvent, bindingFromMouseEvent, conflictFor, hotkeyDisplay, hotkeyParts, isMouseKey, sameHotkey,
    type HotkeyBinding, type TakenBinding,
  } from "./hotkeys";

  let {
    value = $bindable(),
    label,
    description,
    descriptionId,
    defaultBinding,
    taken = [],
  }: {
    value: HotkeyBinding;
    label: string;
    description: string;
    /** Lets the surrounding dialog point aria-describedby at the help text. */
    descriptionId?: string;
    defaultBinding: HotkeyBinding;
    taken?: TakenBinding[];
  } = $props();

  const metaId = $props.id();
  const helpId = $derived(descriptionId ?? `${metaId}-help`);

  let capturing = $state(false);
  let error = $state<string | null>(null);
  let held = $state({ control: false, shift: false, alt: false });
  // A left/right press cancels capture, but the click that follows it would
  // immediately restart capture on the field; skip exactly that click.
  let skipNextClick = false;

  const parts = $derived(hotkeyParts(value));
  const heldParts = $derived([held.control && "Ctrl", held.shift && "Shift", held.alt && "Alt"].filter((part): part is string => Boolean(part)));
  const isDefault = $derived(sameHotkey(value, defaultBinding));
  const mouse = $derived(isMouseKey(value.key));

  function begin() {
    error = null;
    held = { control: false, shift: false, alt: false };
    capturing = true;
  }

  function cancel() {
    capturing = false;
  }

  function toggle() {
    if (skipNextClick) {
      skipNextClick = false;
      return;
    }
    if (capturing) cancel();
    else begin();
  }

  function reset() {
    error = null;
    value = { ...defaultBinding };
  }

  function commit(binding: HotkeyBinding) {
    const owner = conflictFor(binding, taken);
    if (owner) {
      error = `${hotkeyDisplay(binding)} already belongs to ${owner}.`;
      return;
    }
    error = null;
    value = binding;
    capturing = false;
  }

  function swallow(event: Event) {
    event.preventDefault();
    event.stopPropagation();
  }

  function onKeydown(event: KeyboardEvent) {
    swallow(event);
    if (event.repeat) return;
    if (event.code === "Escape" || event.key === "Escape") {
      cancel();
      return;
    }
    const outcome = bindingFromKeyboardEvent(event);
    if ("modifiersOnly" in outcome) {
      held = { control: event.ctrlKey, shift: event.shiftKey, alt: event.altKey };
      return;
    }
    if ("error" in outcome) {
      error = outcome.error;
      return;
    }
    commit(outcome.binding);
  }

  function onKeyup(event: KeyboardEvent) {
    swallow(event);
    held = { control: event.ctrlKey, shift: event.shiftKey, alt: event.altKey };
  }

  function onMousedown(event: MouseEvent) {
    swallow(event);
    if (event.button === 0 || event.button === 2) {
      skipNextClick = event.button === 0;
      cancel();
      return;
    }
    const outcome = bindingFromMouseEvent(event);
    if ("error" in outcome) {
      error = outcome.error;
      return;
    }
    if ("binding" in outcome) commit(outcome.binding);
  }

  function onBlur() {
    cancel();
  }

  $effect(() => {
    if (!capturing) return;
    // Registered global shortcuts never reach the WebView, so the shell releases them while we listen.
    invoke("shortcut_capture", { active: true }).catch((cause: unknown) => console.warn("shortcut_capture(true) failed", cause));
    const options: AddEventListenerOptions = { capture: true };
    window.addEventListener("keydown", onKeydown, options);
    window.addEventListener("keyup", onKeyup, options);
    window.addEventListener("mousedown", onMousedown, options);
    window.addEventListener("mouseup", swallow, options);
    window.addEventListener("auxclick", swallow, options);
    window.addEventListener("contextmenu", swallow, options);
    window.addEventListener("blur", onBlur);
    return () => {
      window.removeEventListener("keydown", onKeydown, options);
      window.removeEventListener("keyup", onKeyup, options);
      window.removeEventListener("mousedown", onMousedown, options);
      window.removeEventListener("mouseup", swallow, options);
      window.removeEventListener("auxclick", swallow, options);
      window.removeEventListener("contextmenu", swallow, options);
      window.removeEventListener("blur", onBlur);
      invoke("shortcut_capture", { active: false }).catch((cause: unknown) => console.warn("shortcut_capture(false) failed", cause));
    };
  });
</script>

<div class="hotkey" class:capturing class:has-error={error !== null}>
  <button
    type="button"
    class="hotkey__field"
    aria-label={label}
    aria-pressed={capturing}
    aria-describedby={`${helpId} ${metaId}`}
    title={capturing ? "Listening for a key or mouse button" : `Change the ${label.toLowerCase()}`}
    onclick={toggle}
  >
    <span class="hotkey__caps">
      {#if capturing}
        {#each heldParts as part (part)}<kbd class="hotkey__cap">{part}</kbd><span class="hotkey__plus">+</span>{/each}
        <kbd class="hotkey__cap hotkey__cap--waiting">{heldParts.length ? "…" : "Press a key"}</kbd>
      {:else}
        {#each parts as part, index (index)}
          {#if index > 0}<span class="hotkey__plus">+</span>{/if}<kbd class="hotkey__cap">{part}</kbd>
        {/each}
      {/if}
    </span>
    <span class="hotkey__hint" aria-hidden="true">
      {#if capturing}<span class="hotkey__pulse"><i class="live-ring" use:phaseLock></i></span>Listening…{:else}{#if mouse}<Mouse size={14} strokeWidth={2} />{:else}<Keyboard size={14} strokeWidth={2} />{/if}Click, then press a key or side button{/if}
    </span>
  </button>
  <p class="hotkey__help" id={helpId}>{description}</p>
  <div class="hotkey__meta" id={metaId} aria-live="polite">
    {#if error}
      <span class="hotkey__error">{error}</span>
    {:else if capturing}
      <span>Press any key or combo · Mouse 4, Mouse 5, and middle click work · Esc cancels</span>
    {:else if !isDefault}
      <button type="button" class="hotkey__reset" onclick={reset}><RotateCcw size={12} strokeWidth={2.2} aria-hidden="true" />Reset to {hotkeyDisplay(defaultBinding)}</button>
    {/if}
  </div>
</div>

<style>
  .hotkey { display: grid; gap: 8px; }

  .hotkey__field {
    width: 100%;
    height: 56px;
    padding: 0 14px 0 12px;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    border: 1px dashed rgba(217, 119, 87, 0.45);
    border-radius: var(--radius-button);
    background: rgba(217, 119, 87, 0.05);
    color: var(--text-2);
    font: inherit;
    cursor: pointer;
    transition: border-color var(--dur-fast) var(--ease-out), background-color var(--dur-fast) var(--ease-out), box-shadow var(--dur-fast) var(--ease-out);
  }

  .hotkey__field:hover { background: rgba(217, 119, 87, 0.09); }
  .capturing .hotkey__field { border-style: solid; border-color: var(--accent); background: rgba(217, 119, 87, 0.1); }
  .has-error .hotkey__field { border-color: var(--danger-line); }

  .hotkey__caps { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 6px; min-width: 0; }

  .hotkey__cap {
    min-width: 28px;
    padding: 7px 11px;
    border: 1px solid rgba(255, 255, 255, 0.16);
    border-bottom-width: 2px;
    border-radius: 6px;
    background: var(--surface-sunken);
    color: var(--text-strong);
    font: 700 15px/1 "Cascadia Code", Consolas, monospace;
    text-align: center;
    white-space: nowrap;
  }

  .hotkey__cap--waiting {
    border-style: dashed;
    border-color: rgba(217, 119, 87, 0.5);
    background: transparent;
    color: var(--accent-text);
    font-size: var(--fs-small);
    font-weight: 600;
  }

  .hotkey__plus { color: var(--text-3); font: 600 12px/1 "Cascadia Code", Consolas, monospace; }

  .hotkey__hint {
    display: inline-flex;
    align-items: center;
    gap: 7px;
    flex-shrink: 0;
    color: var(--text-2);
    font-size: var(--fs-small);
    text-align: right;
  }

  .capturing .hotkey__hint { color: var(--accent-text); }

  .hotkey__pulse {
    position: relative;
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--accent);
  }

  .hotkey__help { margin: 0; color: var(--text-3); font-size: var(--fs-caption); line-height: 1.45; }

  .hotkey__meta {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 6px 12px;
    color: var(--text-3);
    font-size: var(--fs-caption);
    line-height: 1.4;
  }

  .hotkey__meta:not(:has(*)) { display: none; }
  .capturing .hotkey__meta { color: var(--accent-text); }
  .hotkey__error { color: var(--danger-text); }

  .hotkey__reset {
    margin-left: auto;
    padding: 2px 4px;
    display: inline-flex;
    align-items: center;
    gap: 5px;
    border: 0;
    border-radius: var(--radius-kbd);
    background: transparent;
    color: var(--text-3);
    font: inherit;
    font-size: var(--fs-caption);
    cursor: pointer;
    transition: color var(--dur-fast) var(--ease-out);
  }

  .hotkey__reset:hover { color: var(--accent-text); }
</style>
