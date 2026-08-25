import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const tauri = vi.hoisted(() => ({
  invoke: vi.fn(),
  listen: vi.fn(),
  unlisten: vi.fn(),
  eventHandler: undefined as ((event: { payload: { name: string; payload: unknown } }) => void) | undefined,
}));

vi.mock("@tauri-apps/api/core", () => ({ invoke: tauri.invoke }));
vi.mock("@tauri-apps/api/event", () => ({ listen: tauri.listen }));

import { EngineClient, type Snapshot } from "./engine.svelte";

function snapshot(overrides: Partial<Snapshot> = {}): Snapshot {
  return {
    protocolVersion: 1,
    engineVersion: "5.0.12",
    routineState: "Stopped",
    status: { state: "Stopped", detail: "FiveM target loaded.", sampleCount: 0, confidence: 0, debug: null },
    targetValid: true,
    canStart: true,
    targetValidation: "FiveM target ready.",
    targets: null,
    diagnosticsDirectory: "C:\\diagnostics",
    debug: null,
    setupVerification: null,
    lockpicking: {
      observing: false,
      state: "Stopped",
      detail: "Lockpicking observation is stopped.",
      sampleCount: 0,
      confidence: 0,
      captureBackend: "None",
      captureMilliseconds: 0,
      accumulatedFrames: 1,
      spin: null,
      inputEnabled: false,
      vehicleClass: "",
      actionCount: 0,
      spinInputActive: false,
      evidenceDirectory: "",
      observation: { state: "Hidden", confidence: 0, hudCenterX: 0, hudCenterY: 0, hudRadius: 0, target: null, visibleTargetCount: 0, predictedAction: "WAIT", reason: "Lockpicking HUD not found." },
    },
    settings: {
      formatVersion: 9,
      selectedProfile: "fishing",
      startStop: { key: "F10", control: false, shift: false, alt: false },
      lockpickingStartStop: { key: "F9", control: false, shift: false, alt: false },
      emergencyStop: { key: "Pause", control: false, shift: false, alt: false },
      routine: {
        fishingLowerTensionPercent: 55,
        fishingUpperTensionPercent: 68,
        fishingSampleMilliseconds: 40,
        fishingMinimumPulseMilliseconds: 35,
        fishingMaximumPulseMilliseconds: 90,
        fishingMinimumRestMilliseconds: 70,
        fishingCastAccelerationDelayMilliseconds: 5000,
        maximumDurationSeconds: 210,
        collectDelayMilliseconds: 250,
        collectOnTimeout: false,
        inputMode: "Automatic",
        targetWindow: { processId: 3258, processName: "FiveM", windowTitle: "FiveM" },
      },
    },
    ...overrides,
  };
}

beforeEach(() => {
  tauri.invoke.mockReset();
  tauri.listen.mockReset();
  tauri.unlisten.mockReset();
  tauri.eventHandler = undefined;
  tauri.listen.mockImplementation(async (_name, handler) => {
    tauri.eventHandler = handler;
    return tauri.unlisten;
  });
});

afterEach(() => vi.useRealTimers());

describe("EngineClient", () => {
  it("connects from the correlated snapshot response without a ready event", async () => {
    tauri.invoke.mockResolvedValue(snapshot());
    const client = new EngineClient();

    await client.connect();

    expect(client.connected).toBe(true);
    expect(client.snapshot?.canStart).toBe(true);
    expect(client.status.detail).toContain("FiveM");
    expect(tauri.invoke).toHaveBeenCalledWith("engine_command", { command: "snapshot" });
  });

  it("deduplicates concurrent connection attempts and removes its listener", async () => {
    tauri.invoke.mockResolvedValue(snapshot());
    const client = new EngineClient();

    await Promise.all([client.connect(), client.connect()]);
    client.disconnect();

    expect(tauri.listen).toHaveBeenCalledTimes(1);
    expect(tauri.invoke).toHaveBeenCalledTimes(1);
    expect(tauri.unlisten).toHaveBeenCalledTimes(1);
    expect(client.connected).toBe(false);
  });

  it("applies status events and marks an exited sidecar offline", async () => {
    tauri.invoke.mockResolvedValue(snapshot());
    const client = new EngineClient();
    await client.connect();

    tauri.eventHandler?.({
      payload: { name: "status", payload: { state: "Regulating", detail: "Meter locked.", sampleCount: 20, confidence: 0.88, debug: null } },
    });
    expect(client.status.state).toBe("Regulating");
    expect(client.snapshot?.routineState).toBe("Regulating");

    tauri.eventHandler?.({
      payload: { name: "bridge_state", payload: { connected: false, detail: "Local engine exited." } },
    });
    expect(client.connected).toBe(false);
    expect(client.status.detail).toBe("Local engine exited.");
    client.disconnect();
  });

  it("keeps live debug-session evidence on status updates", async () => {
    tauri.invoke.mockResolvedValue(snapshot());
    const client = new EngineClient();
    await client.connect();
    const debug = {
      sessionId: "20260813-220000-abcd",
      active: true,
      stage: "Cast",
      elapsedMilliseconds: 1200,
      eventCount: 12,
      savedFrameCount: 1,
      captureHealth: "Desktop GDI · 24.0 ms",
      prompt: { kind: "None", confidence: 0.61, accepted: false, reason: "Below gate", secondaryConfidence: 0.5 },
      meter: { kind: "Missing", confidence: 0, accepted: false, reason: "No sample", secondaryConfidence: 0 },
      lastEvent: "prompt: sample",
      outcome: "Running",
    };

    tauri.eventHandler?.({
      payload: { name: "status", payload: { state: "Casting", detail: "Scanning", sampleCount: 10, confidence: 0.61, debug } },
    });

    expect(client.status.debug?.stage).toBe("Cast");
    expect(client.snapshot?.debug?.prompt.reason).toBe("Below gate");
  });

  it("surfaces a rejected backend command instead of reporting write success", async () => {
    tauri.invoke.mockResolvedValueOnce(snapshot()).mockRejectedValueOnce("FiveM was not selected.");
    const client = new EngineClient();
    await client.connect();

    await expect(client.command("start")).rejects.toBe("FiveM was not selected.");
    expect(client.error).toContain("FiveM was not selected");
  });

  it("discovers FiveM windows without changing desktop focus", async () => {
    const target = {
      processId: 3258,
      processName: "FiveM_b3258_GTAProcess",
      windowTitle: "FiveM",
      isForeground: false,
      isMinimized: false,
      isSelected: false,
    };
    tauri.invoke.mockResolvedValue(snapshot({ targets: [target] }));
    const client = new EngineClient();

    await expect(client.discoverTargets()).resolves.toEqual([target]);

    expect(tauri.invoke).toHaveBeenCalledWith("engine_command", { command: "list_targets" });
  });

  it("selects a discovered target by its validated process ID", async () => {
    tauri.invoke.mockResolvedValue(snapshot());
    const client = new EngineClient();

    await client.selectTarget(3258);

    expect(tauri.invoke).toHaveBeenCalledWith("engine_command", {
      command: "select_target",
      targetProcessId: 3258,
    });
  });

  it("starts the explicitly gated Class C lockpicking mode", async () => {
    tauri.invoke.mockResolvedValue(snapshot());
    const client = new EngineClient();

    await client.setLockpicking("classC");

    expect(tauri.invoke).toHaveBeenCalledWith("engine_command", {
      command: "start_lockpicking_class_c",
    });
  });

  it("rejects a stale sidecar protocol and cleans up the listener", async () => {
    tauri.invoke.mockResolvedValue(snapshot({ protocolVersion: 0 }));
    const client = new EngineClient();

    await expect(client.connect()).rejects.toThrow("protocol mismatch");

    expect(client.connected).toBe(false);
    expect(client.error).toContain("protocol mismatch");
    expect(tauri.unlisten).toHaveBeenCalledTimes(1);
  });

  it("ignores a connection failure after the client has been disconnected", async () => {
    let rejectSnapshot: (reason?: unknown) => void;
    tauri.invoke.mockReturnValueOnce(new Promise((_, reject) => rejectSnapshot = reject));
    const client = new EngineClient();

    const connecting = client.connect();
    await Promise.resolve();
    client.disconnect();
    rejectSnapshot!("The engine closed.");

    await expect(connecting).resolves.toBeUndefined();
    expect(client.error).toBeNull();
    expect(client.connected).toBe(false);
  });

  it("reconnects automatically after the sidecar exits", async () => {
    vi.useFakeTimers();
    tauri.invoke.mockResolvedValue(snapshot());
    const client = new EngineClient();
    await client.connect();

    tauri.eventHandler?.({
      payload: { name: "bridge_state", payload: { connected: false, detail: "Local engine exited." } },
    });
    await vi.advanceTimersByTimeAsync(800);

    expect(tauri.invoke).toHaveBeenCalledTimes(2);
    expect(tauri.listen).toHaveBeenCalledTimes(2);
    expect(client.connected).toBe(true);
    client.disconnect();
  });
});
