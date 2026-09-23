import type { HotkeyBinding } from "./engine.svelte";

export type { HotkeyBinding };

/** Mouse buttons the shell can watch through its low-level mouse hook. Left and right stay with the game. */
const MOUSE_KEYS: Record<string, string> = {
  MouseMiddle: "Middle Mouse",
  MouseX1: "Mouse 4",
  MouseX2: "Mouse 5",
};

/** `KeyboardEvent.code` values the Tauri global-shortcut plugin can register. Mirrors global-hotkey's parser. */
const KEYBOARD_CODES = new Set<string>([
  "Backquote", "Backslash", "BracketLeft", "BracketRight", "Comma", "Equal", "Minus", "Period", "Quote", "Semicolon", "Slash",
  "Backspace", "CapsLock", "Enter", "Space", "Tab", "Delete", "End", "Home", "Insert", "PageDown", "PageUp", "PrintScreen", "ScrollLock", "Pause",
  "ArrowDown", "ArrowLeft", "ArrowRight", "ArrowUp", "NumLock",
  "NumpadAdd", "NumpadDecimal", "NumpadDivide", "NumpadEnter", "NumpadEqual", "NumpadMultiply", "NumpadSubtract",
  "AudioVolumeDown", "AudioVolumeUp", "AudioVolumeMute", "MediaPlayPause", "MediaStop", "MediaTrackNext", "MediaTrackPrevious",
  ...Array.from({ length: 26 }, (_, index) => `Key${String.fromCharCode(65 + index)}`),
  ...Array.from({ length: 10 }, (_, index) => `Digit${index}`),
  ...Array.from({ length: 10 }, (_, index) => `Numpad${index}`),
  ...Array.from({ length: 24 }, (_, index) => `F${index + 1}`),
]);

const MODIFIER_CODES = new Set(["ControlLeft", "ControlRight", "ShiftLeft", "ShiftRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight"]);

const KEY_LABELS: Record<string, string> = {
  NumpadAdd: "Num +", NumpadSubtract: "Num -", NumpadMultiply: "Num *", NumpadDivide: "Num /", NumpadDecimal: "Num .", NumpadEnter: "Num Enter", NumpadEqual: "Num =",
  Backquote: "`", Minus: "-", Equal: "=", BracketLeft: "[", BracketRight: "]", Backslash: "\\", Semicolon: ";", Quote: "'", Comma: ",", Period: ".", Slash: "/",
  ArrowUp: "Up", ArrowDown: "Down", ArrowLeft: "Left", ArrowRight: "Right",
  Pause: "Pause / Break", Return: "Enter", CapsLock: "Caps Lock", PageUp: "Page Up", PageDown: "Page Down", PrintScreen: "Print Screen", ScrollLock: "Scroll Lock", NumLock: "Num Lock",
  AudioVolumeUp: "Volume Up", AudioVolumeDown: "Volume Down", AudioVolumeMute: "Mute", MediaPlayPause: "Play / Pause", MediaTrackNext: "Next Track", MediaTrackPrevious: "Previous Track", MediaStop: "Media Stop",
};

export const RESERVED_KEYS: Record<string, string> = {
  F8: "F8 is reserved for the FiveM console.",
  Escape: "Escape cancels capture, so it can't be a shortcut.",
};

export const LEFT_RIGHT_MOUSE_MESSAGE = "Left and right mouse buttons stay with the game.";

export function isMouseKey(key: string): boolean {
  return key in MOUSE_KEYS;
}

/** Human label for one stored key. Accepts the W3C code vocabulary plus the legacy `D1` / `Return` spellings. */
export function describeKey(key: string): string {
  if (key in MOUSE_KEYS) return MOUSE_KEYS[key];
  if (/^Key[A-Z]$/.test(key)) return key.slice(3);
  if (/^Digit\d$/.test(key)) return key.slice(5);
  if (/^D\d$/.test(key)) return key.slice(1);
  if (/^Numpad\d$/.test(key)) return `Num ${key.slice(6)}`;
  return KEY_LABELS[key] ?? key;
}

export function hotkeyParts(binding: HotkeyBinding): string[] {
  return [binding.control && "Ctrl", binding.shift && "Shift", binding.alt && "Alt", describeKey(binding.key)].filter((part): part is string => Boolean(part));
}

export function hotkeyDisplay(binding: HotkeyBinding | null | undefined, fallback = "F10"): string {
  if (!binding) return fallback;
  return hotkeyParts(binding).join(" + ");
}

export function sameHotkey(left: HotkeyBinding, right: HotkeyBinding): boolean {
  return left.key.toLowerCase() === right.key.toLowerCase()
    && left.control === right.control
    && left.shift === right.shift
    && left.alt === right.alt;
}

export type CaptureOutcome =
  | { binding: HotkeyBinding }
  | { error: string }
  | { modifiersOnly: true };

type KeyboardLike = Pick<KeyboardEvent, "code" | "ctrlKey" | "shiftKey" | "altKey" | "metaKey">;
type MouseLike = Pick<MouseEvent, "button" | "ctrlKey" | "shiftKey" | "altKey">;

export function bindingFromKeyboardEvent(event: KeyboardLike): CaptureOutcome {
  if (MODIFIER_CODES.has(event.code)) return { modifiersOnly: true };
  if (event.metaKey) return { error: "The Windows key can't be part of a shortcut." };
  if (event.code in RESERVED_KEYS) return { error: RESERVED_KEYS[event.code] };
  if (!KEYBOARD_CODES.has(event.code)) return { error: "That key can't be used as a global shortcut." };
  return { binding: { key: event.code, control: event.ctrlKey, shift: event.shiftKey, alt: event.altKey } };
}

export function bindingFromMouseEvent(event: MouseLike): CaptureOutcome {
  const key = event.button === 1 ? "MouseMiddle" : event.button === 3 ? "MouseX1" : event.button === 4 ? "MouseX2" : null;
  if (!key) return { error: event.button === 0 || event.button === 2 ? LEFT_RIGHT_MOUSE_MESSAGE : "That mouse button isn't supported." };
  return { binding: { key, control: event.ctrlKey, shift: event.shiftKey, alt: event.altKey } };
}

export type TakenBinding = { binding: HotkeyBinding; owner: string };

/** Name of the control already using this binding, or null when it is free. */
export function conflictFor(binding: HotkeyBinding, taken: TakenBinding[]): string | null {
  return taken.find(entry => sameHotkey(entry.binding, binding))?.owner ?? null;
}
