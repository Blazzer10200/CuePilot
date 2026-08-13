import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

export type RoutineState = "Stopped" | "Armed" | "Regulating" | "Collecting" | "Stowing" | "Casting" | "Faulted";

export interface RoutineSettings {
  fishingLowerTensionPercent: number;
  fishingUpperTensionPercent: number;
  fishingSampleMilliseconds: number;
  fishingMinimumPulseMilliseconds: number;
  fishingMaximumPulseMilliseconds: number;
  fishingMinimumRestMilliseconds: number;
  maximumDurationSeconds: number;
  collectDelayMilliseconds: number;
  collectOnTimeout: boolean;
  inputMode: "Automatic" | "Foreground" | "Application";
  targetWindow: { processName: string; windowTitle: string };
}

export interface AppSettings {
  formatVersion: number;
  selectedProfile: string;
  emergencyStop: { key: string; control: boolean; shift: boolean; alt: boolean };
  routine: RoutineSettings;
}

export interface Snapshot {
  routineState: RoutineState;
  settings: AppSettings;
  diagnosticsDirectory: string;
}

export interface LiveStatus {
  state: RoutineState;
  detail: string;
  sampleCount: number;
  confidence: number;
}

export class EngineClient {
  snapshot = $state<Snapshot | null>(null);
  status = $state<LiveStatus>({ state: "Stopped", detail: "Connecting to local engine…", sampleCount: 0, confidence: 0 });
  connected = $state(false);
  error = $state<string | null>(null);
  onEvent: ((name: string) => void) | undefined;

  async connect() {
    await listen<{ name: string; payload: unknown }>("engine://event", ({ payload }) => this.consume(payload));
    await invoke<void>("engine_command", { command: "snapshot" });
  }

  async command(command: "start" | "stop" | "capture_target", delayMilliseconds = 0) {
    this.error = null;
    await invoke<void>("engine_command", { command, delayMilliseconds });
  }

  async saveSettings(settings: AppSettings) {
    this.error = null;
    await invoke<void>("engine_command", { command: "save_settings", settings });
  }

  private consume(message: { name: string; payload: unknown }) {
    if (message.name === "status") this.status = message.payload as LiveStatus;
    if (message.name === "ready" || message.name === "target" || message.name === "settings") {
      this.snapshot = message.payload as Snapshot;
      this.status.state = this.snapshot.routineState;
      this.status.detail = this.snapshot.settings.routine.targetWindow.processName
        ? "Target loaded. Ready when you are."
        : "Select FiveM as the target before starting.";
      this.connected = true;
    }
    if (message.name === "fault") this.error = (message.payload as { detail: string }).detail;
    this.onEvent?.(message.name);
  }
}
