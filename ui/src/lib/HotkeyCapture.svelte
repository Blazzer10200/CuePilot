<script lang="ts">
  import { invoke } from "@tauri-apps/api/core";
  import { Keyboard, Mouse, RotateCcw } from "@lucide/svelte";
  import {
    bindingFromKeyboardEvent, bindingFromMouseEvent, conflictFor, hotkeyDisplay, hotkeyParts, isMouseKey, sameHotkey,
    type HotkeyBinding, type TakenBinding,
  } from "./hotkeys";

  let {
    value = $bindable(),
    label,
    title,
    description,
    defaultBinding,
    taken = [],
  }: {
    value: HotkeyBinding;
    label: string;
    title: string;
    description: string;
    defaultBinding: HotkeyBinding;
    taken?: TakenBinding[];
  } = $props();

  const metaId = $props.id();

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

<div class="shortcut-control hotkey" class:capturing class:has-error={error !== null}>
  <div class="hotkey__copy"><strong>{title}</strong><small>{description}</small></div>
  <button
    type="button"
    class="hotkey__field"
    aria-label={label}
    aria-pressed={capturing}
    aria-describedby={metaId}
    title={capturing ? "Listening for a key or mouse button" : `Change the ${label.toLowerCase()}`}
    onclick={toggle}
  >
    <span class="hotkey__icon" aria-hidden="true">
      {#if capturing}<span class="hotkey__pulse"></span>{:else if mouse}<Mouse size={13} strokeWidth={2} />{:else}<Keyboard size={13} strokeWidth={2} />{/if}
    </span>
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
  </button>
  <div class="hotkey__meta" id={metaId} aria-live="polite">
    {#if error}
      <span class="hotkey__error">{error}</span>
    {:else if capturing}
      <span>Press any key or combo · Mouse 4, Mouse 5, and middle click work · Esc cancels</span>
    {:else}
      <span>Click to change. Left and right click stay with the game.</span>
      {#if !isDefault}
        <button type="button" class="hotkey__reset" onclick={reset}><RotateCcw size={11} strokeWidth={2.2} aria-hidden="true" />Reset to {hotkeyDisplay(defaultBinding)}</button>
      {/if}
    {/if}
  </div>
</div>

<style>
  .hotkey {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas: "copy field" "meta meta";
    align-items: center;
    row-gap: 8px;
    column-gap: 14px;
  }

  .hotkey.capturing {
    border-color: rgba(217, 119, 87, 0.5);
    background: rgba(217, 119, 87, 0.06);
  }

  .hotkey.has-error {
    border-color: rgba(229, 105, 95, 0.42);
  }

  .hotkey__copy {
    grid-area: copy;
    min-width: 0;
    display: grid;
    gap: 3px;
  }

  .hotkey__field {
    grid-area: field;
    min-height: 34px;
    max-width: 100%;
    padding: 0 10px 0 9px;
    display: inline-flex;
    align-items: center;
    gap: 8px;
    border: 1px solid rgba(217, 119, 87, 0.22);
    border-radius: 8px;
    background: #272624;
    color: var(--accent);
    cursor: pointer;
    outline: none;
    transition: border-color var(--duration-fast) var(--ease-standard), background var(--duration-fast) var(--ease-standard), box-shadow var(--duration-fast) var(--ease-standard);
  }

  .hotkey__field:hover {
    border-color: rgba(217, 119, 87, 0.4);
    background: #2b2a28;
  }

  .hotkey__field:focus-visible {
    border-color: rgba(217, 119, 87, 0.55);
    box-shadow: 0 0 0 3px rgba(217, 119, 87, 0.1);
  }

  .capturing .hotkey__field {
    border-color: var(--accent);
    border-style: dashed;
    background: rgba(217, 119, 87, 0.08);
    box-shadow: 0 0 0 3px rgba(217, 119, 87, 0.12);
  }

  .hotkey__icon {
    width: 14px;
    height: 14px;
    flex: 0 0 auto;
    display: grid;
    place-items: center;
    color: #a89e96;
  }

  .hotkey__pulse {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--accent);
    box-shadow: 0 0 0 0 rgba(217, 119, 87, 0.5);
    animation: hotkey-pulse 1.3s ease-out infinite;
  }

  @keyframes hotkey-pulse {
    0% { box-shadow: 0 0 0 0 rgba(217, 119, 87, 0.5); }
    70% { box-shadow: 0 0 0 6px rgba(217, 119, 87, 0); }
    100% { box-shadow: 0 0 0 0 rgba(217, 119, 87, 0); }
  }

  .hotkey__caps {
    display: inline-flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 5px;
  }

  .hotkey__cap {
    min-width: 24px;
    padding: 4px 7px 3px;
    border: 1px solid rgba(255, 255, 255, 0.14);
    border-bottom-width: 2px;
    border-radius: 5px;
    background: linear-gradient(180deg, #34322f, #2a2927);
    color: #f0ede9;
    font: 650 11px/1 "Cascadia Code", Consolas, monospace;
    letter-spacing: 0.01em;
    text-align: center;
    white-space: nowrap;
  }

  .hotkey__cap--waiting {
    border-style: dashed;
    border-color: rgba(217, 119, 87, 0.5);
    background: transparent;
    color: var(--accent);
    font-weight: 600;
  }

  .hotkey__plus {
    color: #7d7973;
    font: 600 10px/1 "Cascadia Code", Consolas, monospace;
  }

  .hotkey__meta {
    grid-area: meta;
    min-height: 14px;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 6px 12px;
    color: #7f7b76;
    font-size: 10px;
    line-height: 1.35;
  }

  .capturing .hotkey__meta {
    color: #c2a597;
  }

  .hotkey__error {
    color: var(--danger);
  }

  .hotkey__reset {
    margin-left: auto;
    padding: 2px 6px 2px 5px;
    display: inline-flex;
    align-items: center;
    gap: 4px;
    border: 1px solid transparent;
    border-radius: 5px;
    background: transparent;
    color: #a19f9b;
    font: 600 10px/1 inherit;
    font-family: inherit;
    cursor: pointer;
    transition: color var(--duration-fast) var(--ease-standard), background var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard);
  }

  .hotkey__reset:hover {
    color: var(--text);
    background: rgba(255, 255, 255, 0.05);
    border-color: rgba(255, 255, 255, 0.08);
  }

  .hotkey__reset:focus-visible {
    outline: none;
    box-shadow: var(--focus-ring);
  }

  @media (max-width: 560px) {
    .hotkey {
      grid-template-columns: minmax(0, 1fr);
      grid-template-areas: "copy" "field" "meta";
    }

    .hotkey__field {
      justify-self: start;
    }
  }
</style>
