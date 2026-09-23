import { describe, expect, it } from "vitest";

import { bindingFromKeyboardEvent, bindingFromMouseEvent, conflictFor, describeKey, hotkeyDisplay, isMouseKey, sameHotkey } from "./hotkeys";

const binding = (key: string, extra: Partial<{ control: boolean; shift: boolean; alt: boolean }> = {}) => ({ key, control: false, shift: false, alt: false, ...extra });

describe("hotkey display", () => {
  it("labels stored keys the way a player reads them", () => {
    expect(describeKey("F10")).toBe("F10");
    expect(describeKey("KeyA")).toBe("A");
    expect(describeKey("Digit1")).toBe("1");
    expect(describeKey("D1")).toBe("1");
    expect(describeKey("Return")).toBe("Enter");
    expect(describeKey("Numpad5")).toBe("Num 5");
    expect(describeKey("NumpadAdd")).toBe("Num +");
    expect(describeKey("Pause")).toBe("Pause / Break");
    expect(describeKey("MouseX1")).toBe("Mouse 4");
    expect(describeKey("MouseX2")).toBe("Mouse 5");
    expect(describeKey("MouseMiddle")).toBe("Middle Mouse");
  });

  it("joins modifiers in the shell's order and falls back when unset", () => {
    expect(hotkeyDisplay(binding("KeyF", { control: true, alt: true }))).toBe("Ctrl + Alt + F");
    expect(hotkeyDisplay(null)).toBe("F10");
    expect(hotkeyDisplay(undefined, "F7")).toBe("F7");
    expect(isMouseKey("MouseMiddle")).toBe(true);
    expect(isMouseKey("KeyM")).toBe(false);
  });
});

describe("keyboard capture", () => {
  const key = (code: string, extra: Partial<{ ctrlKey: boolean; shiftKey: boolean; altKey: boolean; metaKey: boolean }> = {}) =>
    ({ code, ctrlKey: false, shiftKey: false, altKey: false, metaKey: false, ...extra });

  it("turns a key press into a binding with the held modifiers", () => {
    expect(bindingFromKeyboardEvent(key("KeyG", { ctrlKey: true, shiftKey: true }))).toEqual({ binding: binding("KeyG", { control: true, shift: true }) });
    expect(bindingFromKeyboardEvent(key("F13"))).toEqual({ binding: binding("F13") });
    expect(bindingFromKeyboardEvent(key("MediaPlayPause"))).toEqual({ binding: binding("MediaPlayPause") });
  });

  it("waits while only modifiers are held", () => {
    expect(bindingFromKeyboardEvent(key("ControlLeft", { ctrlKey: true }))).toEqual({ modifiersOnly: true });
    expect(bindingFromKeyboardEvent(key("ShiftRight", { shiftKey: true }))).toEqual({ modifiersOnly: true });
  });

  it("rejects reserved, unsupported, and Windows-key combinations", () => {
    expect(bindingFromKeyboardEvent(key("F8"))).toEqual({ error: "F8 is reserved for the FiveM console." });
    expect(bindingFromKeyboardEvent(key("Escape"))).toMatchObject({ error: expect.stringContaining("Escape") });
    expect(bindingFromKeyboardEvent(key("KeyA", { metaKey: true }))).toMatchObject({ error: expect.stringContaining("Windows key") });
    expect(bindingFromKeyboardEvent(key(""))).toMatchObject({ error: expect.stringContaining("can't be used") });
    expect(bindingFromKeyboardEvent(key("ContextMenu"))).toMatchObject({ error: expect.stringContaining("can't be used") });
  });
});

describe("mouse capture", () => {
  const mouse = (button: number, extra: Partial<{ ctrlKey: boolean; shiftKey: boolean; altKey: boolean }> = {}) => ({ button, ctrlKey: false, shiftKey: false, altKey: false, ...extra });

  it("binds the side and middle buttons, with modifiers", () => {
    expect(bindingFromMouseEvent(mouse(3))).toEqual({ binding: binding("MouseX1") });
    expect(bindingFromMouseEvent(mouse(4, { ctrlKey: true }))).toEqual({ binding: binding("MouseX2", { control: true }) });
    expect(bindingFromMouseEvent(mouse(1))).toEqual({ binding: binding("MouseMiddle") });
  });

  it("keeps left and right click with the game", () => {
    expect(bindingFromMouseEvent(mouse(0))).toEqual({ error: "Left and right mouse buttons stay with the game." });
    expect(bindingFromMouseEvent(mouse(2))).toEqual({ error: "Left and right mouse buttons stay with the game." });
    expect(bindingFromMouseEvent(mouse(7))).toMatchObject({ error: expect.stringContaining("isn't supported") });
  });
});

describe("conflicts", () => {
  it("compares bindings case-insensitively including modifiers", () => {
    expect(sameHotkey(binding("f10"), binding("F10"))).toBe(true);
    expect(sameHotkey(binding("F10", { control: true }), binding("F10"))).toBe(false);
  });

  it("names the control that already owns a binding", () => {
    const taken = [{ binding: binding("F7"), owner: "Pickpocket Start / Stop" }, { binding: binding("Pause"), owner: "Emergency stop" }];
    expect(conflictFor(binding("F7"), taken)).toBe("Pickpocket Start / Stop");
    expect(conflictFor(binding("Pause"), taken)).toBe("Emergency stop");
    expect(conflictFor(binding("MouseX1"), taken)).toBeNull();
  });
});
