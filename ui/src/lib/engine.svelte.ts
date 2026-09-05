import { invoke } from "@tauri-apps/api/core";
import { listen, type UnlistenFn } from "@tauri-apps/api/event";

export type RoutineState = "Stopped" | "Armed" | "Regulating" | "Collecting" | "Stowing" | "Casting" | "Faulted";

export interface RoutineSettings {
  fishingLowerTensionPercent: number;
  fishingUpperTensionPercent: number;
  fishingSampleMilliseconds: number;
  fishingMinimumPulseMilliseconds: number;
  fishingMaximumPulseMilliseconds: number;
  fishingMinimumRestMilliseconds: number;
  fishingCastAccelerationDelayMilliseconds: number;
  maximumDurationSeconds: number;
  collectDelayMilliseconds: number;
  collectOnTimeout: boolean;
  inputMode: "Automatic" | "Foreground";
  targetWindow: { processId: number; processName: string; windowTitle: string };
}

export interface TargetCandidate {
  processId: number;
  processName: string;
  windowTitle: string;
  isForeground: boolean;
  isMinimized: boolean;
  isSelected: boolean;
}

export interface HotkeyBinding {
  key: string;
  control: boolean;
  shift: boolean;
  alt: boolean;
}

export interface AppSettings {
  formatVersion: number;
  selectedProfile: string;
  startStop: HotkeyBinding;
  lockpickingStartStop: HotkeyBinding;
  pickpocketStartStop?: HotkeyBinding;
  emergencyStop: HotkeyBinding;
  routine: RoutineSettings;
}

export interface Snapshot {
  protocolVersion: number;
  engineVersion: string;
  routineState: RoutineState;
  status: LiveStatus;
  targetValid: boolean;
  canStart: boolean;
  targetValidation: string;
  targets: TargetCandidate[] | null;
  settings: AppSettings;
  diagnosticsDirectory: string;
  debug: FishingDebugSnapshot | null;
  lockpicking: LockpickingObserveStatus;
  pickpocket?: PickpocketObserveStatus;
  setupVerification: FishingSetupVerification | null;
}

export interface FishingSetupCheck {
  passed: boolean;
  detail: string;
}

export type PickpocketColor = "White" | "Purple" | "Red" | "PaleGreen" | "Blue" | "Yellow";
export type PickpocketPolicy = "Widest" | "PurpleBlueWhite" | "RarestFirst" | "Custom" | PickpocketColor;
export type PickpocketInputMode = "Observe" | "SingleAttempt" | "PrecisionAttempt";
export interface PickpocketTiming { redAdvanceMs: number; yellowAdvanceMs: number; customPriority?: PickpocketColor[]; itemPriority?: string[]; }
export interface PickpocketRecentAttempt {
  id: string; endedAtUnixMs: number; outcome: "Grabbed" | "Missed" | "Ended";
  color: PickpocketColor | null; widthPixels: number | null; offsetPixels: number | null; automaticPresses: number;
  itemName?: string | null; redAdvanceMs?: number | null; yellowAdvanceMs?: number | null;
  engineVersion?: string | null; sessionId?: string | null; inputMode?: string | null; targetPolicy?: string | null;
}
export interface PickpocketObserveStatus {
  recentAttempts?: PickpocketRecentAttempt[];
  sessionStateError?: string | null;
  customPriority?: PickpocketColor[];
  itemPriority?: string[];
  inputMode?: PickpocketInputMode;
  redAdvanceMs?: number;
  yellowAdvanceMs?: number;
  inputArmed?: boolean;
  automatedPressCount?: number;
  inputDelivery?: { state: string; detail: string; plannedMs: number | null; keyDownMs: number | null; keyUpMs: number | null } | null;
  observing: boolean;
  state: string;
  detail: string;
  sampleCount: number;
  captureMilliseconds: number;
  analysisMilliseconds: number;
  frameAgeMilliseconds: number | null;
  captureBackend: string;
  selectedBandIndex: number | null;
  targetPolicy: PickpocketPolicy;
  predictedPressCount: number;
  cooldownUntilUnixMs: number;
  evidenceDirectory: string;
  attempt: number;
  manualSpacePressCount?: number;
  debug?: { state: string; recordsSaved: number; recordsDropped: number; imagesSaved: number; imagesSkipped: number; error: string | null; report: string;
    result?: { state: string; color: string; widthPixels: number; offsetPixels: number | null } | null } | null;
  observation: {
    state: string;
    bar: { x: number; y: number; width: number; height: number };
    markerX: number;
    bands: { color: PickpocketColor; left: number; right: number; itemName?: string | null }[];
    confidence: number;
    reason: string;
  };
  prediction: { canSchedule: boolean; speedPixelsPerSecond: number; reason: string } | null;
}

export interface FishingSetupVerification {
  target: FishingSetupCheck;
  input: FishingSetupCheck;
  capture: FishingSetupCheck;
  captureBackend: string;
  captureMilliseconds: number;
  windowWidth: number;
  windowHeight: number;
  ready: boolean;
  detail: string;
}

export type LockpickingVisualState = "Hidden" | "Numbered" | "Intermediate" | "Spin" | "Open" | "Unexpected";
export type LockpickingTargetPhase = "None" | "Approaching" | "Ready";

export interface LockpickingTargetObservation {
  centerX: number;
  centerY: number;
  approachRadius: number;
  phase: LockpickingTargetPhase;
  confidence: number;
  number: number | null;
  approachRatio: number;
  radialVelocity: number;
  timeToReadyMilliseconds: number | null;
  fillDensity: number;
}

export interface LockpickingObservation {
  state: LockpickingVisualState;
  confidence: number;
  hudCenterX: number;
  hudCenterY: number;
  hudRadius: number;
  target: LockpickingTargetObservation | null;
  visibleTargetCount: number;
  predictedAction: string;
  reason: string;
}

export interface LockpickingSpinTelemetry {
  cursorVisible: boolean;
  cursorX: number;
  cursorY: number;
  angleDegrees: number;
  radiusRatio: number;
  angularVelocityDegreesPerSecond: number;
  clockwiseTravelDegrees: number;
  elapsedMilliseconds: number;
  capturedFrames: number;
}

export interface LockpickingObserveStatus {
  observing: boolean;
  state: "Stopped" | "Waiting" | "Searching" | "Tracking" | "Faulted";
  detail: string;
  sampleCount: number;
  confidence: number;
  captureBackend: string;
  captureMilliseconds: number;
  evidenceDirectory: string;
  observation: LockpickingObservation;
  accumulatedFrames: number;
  spin: LockpickingSpinTelemetry | null;
  inputEnabled: boolean;
  vehicleClass: string;
  actionCount: number;
  spinInputActive: boolean;
}

export interface FishingDebugDecision {
  kind: string;
  confidence: number;
  accepted: boolean;
  reason: string;
  secondaryConfidence: number;
}

export interface FishingDebugSnapshot {
  sessionId: string;
  active: boolean;
  stage: string;
  elapsedMilliseconds: number;
  eventCount: number;
  savedFrameCount: number;
  captureHealth: string;
  prompt: FishingDebugDecision;
  meter: FishingDebugDecision;
  lastEvent: string;
  outcome: string;
}

export interface LiveStatus {
  state: RoutineState;
  detail: string;
  sampleCount: number;
  confidence: number;
  debug: FishingDebugSnapshot | null;
}

export class EngineClient {
  snapshot = $state<Snapshot | null>(null);
  status = $state<LiveStatus>({ state: "Stopped", detail: "Connecting to local engine…", sampleCount: 0, confidence: 0, debug: null });
  connected = $state(false);
  error = $state<string | null>(null);
  private unlisten: UnlistenFn | null = null;
  private connecting: Promise<void> | null = null;
  private connectionGeneration = 0;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private shouldReconnect = false;

  async connect() {
    this.shouldReconnect = true;
    if (this.connecting) return this.connecting;
    this.connecting = this.connectInternal().finally(() => this.connecting = null);
    return this.connecting;
  }

  disconnect() {
    this.shouldReconnect = false;
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
    this.teardownConnection();
  }

  private teardownConnection() {
    this.connectionGeneration += 1;
    this.unlisten?.();
    this.unlisten = null;
    this.connected = false;
  }

  async command(command: "start" | "stop") {
    this.error = null;
    try {
      const snapshot = await invoke<Snapshot>("engine_command", { command });
      this.applySnapshot(snapshot);
      return snapshot;
    } catch (error) {
      this.error = String(error);
      throw error;
    }
  }

  async setLockpicking(mode: "observe" | "classC" | "stop") {
    this.error = null;
    try {
      const snapshot = await invoke<Snapshot>("engine_command", {
        command: mode === "observe"
          ? "start_lockpicking_observe"
          : mode === "classC"
            ? "start_lockpicking_class_c"
            : "stop_lockpicking_observe",
      });
      this.applySnapshot(snapshot);
      return snapshot;
    } catch (error) {
      this.error = String(error);
      throw error;
    }
  }

  async setPickpocket(mode: "observe" | "stop", policy: PickpocketPolicy = "Widest", inputMode: PickpocketInputMode = "Observe") {
    this.error = null;
    try {
      if (mode === "observe") {
        this.applySnapshot(await invoke<Snapshot>("engine_command", {
          command: "configure_pickpocket", settings: { targetPolicy: policy, inputMode },
        }));
      }
      const snapshot = await invoke<Snapshot>("engine_command", {
        command: mode === "observe" ? "start_pickpocket_observe" : "stop_pickpocket_observe",
      });
      this.applySnapshot(snapshot);
      return snapshot;
    } catch (error) {
      this.error = String(error);
      throw error;
    }
  }

  async configurePickpocket(policy: PickpocketPolicy, inputMode: PickpocketInputMode = "Observe", timing?: PickpocketTiming) {
    this.error = null;
    try {
      this.applySnapshot(await invoke<Snapshot>("engine_command", {
        command: "configure_pickpocket", settings: { targetPolicy: policy, inputMode, ...timing },
      }));
    } catch (error) { this.error = String(error); throw error; }
  }

  async discoverTargets() {
    this.error = null;
    try {
      const snapshot = await invoke<Snapshot>("engine_command", { command: "list_targets" });
      this.applySnapshot(snapshot);
      return snapshot.targets ?? [];
    } catch (error) {
      this.error = String(error);
      throw error;
    }
  }

  async selectTarget(processId: number) {
    this.error = null;
    try {
      const snapshot = await invoke<Snapshot>("engine_command", { command: "select_target", targetProcessId: processId });
      this.applySnapshot(snapshot);
      return snapshot;
    } catch (error) {
      this.error = String(error);
      throw error;
    }
  }

  async verifySetup() {
    this.error = null;
    try {
      const snapshot = await invoke<Snapshot>("engine_command", { command: "verify_setup" });
      this.applySnapshot(snapshot);
      return snapshot.setupVerification;
    } catch (error) {
      this.error = String(error);
      throw error;
    }
  }

  async saveSettings(settings: AppSettings) {
    this.error = null;
    try {
      const snapshot = await invoke<Snapshot>("engine_command", { command: "save_settings", settings });
      this.applySnapshot(snapshot);
      return snapshot;
    } catch (error) {
      this.error = String(error);
      throw error;
    }
  }

  private async connectInternal() {
    this.teardownConnection();
    const generation = this.connectionGeneration;
    this.error = null;
    this.status = { state: "Stopped", detail: "Connecting to local engine…", sampleCount: 0, confidence: 0, debug: null };
    const unlisten = await listen<{ name: string; payload: unknown }>("engine://event", ({ payload }) => this.consume(payload));
    if (generation !== this.connectionGeneration) {
      unlisten();
      return;
    }
    this.unlisten = unlisten;
    try {
      const snapshot = await invoke<Snapshot>("engine_command", { command: "snapshot" });
      if (generation !== this.connectionGeneration) return;
      this.applySnapshot(snapshot);
    } catch (error) {
      if (generation !== this.connectionGeneration) return;

      this.teardownConnection();
      this.error = String(error);
      if (!this.error.toLowerCase().includes("protocol mismatch")) this.scheduleReconnect();
      throw error;
    }
  }

  private consume(message: { name: string; payload: unknown }) {
    if (message.name === "status") {
      this.status = message.payload as LiveStatus;
      if (this.snapshot) {
        this.snapshot = { ...this.snapshot, routineState: this.status.state, status: this.status, debug: this.status.debug };
      }
    }
    if (message.name === "lockpicking_status") {
      const lockpicking = message.payload as LockpickingObserveStatus;
      if (this.snapshot) this.snapshot = { ...this.snapshot, lockpicking };
    }
    if (message.name === "pickpocket_status") {
      const pickpocket = message.payload as PickpocketObserveStatus;
      if (this.snapshot) this.snapshot = { ...this.snapshot, pickpocket };
    }
    if (message.name === "ready" || message.name === "target" || message.name === "settings") {
      try {
        this.applySnapshot(message.payload as Snapshot);
      } catch (error) {
        this.connected = false;
        this.error = String(error);
      }
    }
    if (message.name === "fault") this.error = (message.payload as { detail: string }).detail;
    if (message.name === "bridge_state") {
      const state = message.payload as { connected: boolean; detail: string };
      if (!state.connected) {
        this.connected = false;
        this.status = { ...this.status, detail: state.detail };
        this.scheduleReconnect();
      }
    }
  }

  private applySnapshot(snapshot: Snapshot) {
    if (snapshot.protocolVersion !== 1) {
      throw new Error(`Engine/UI protocol mismatch. Expected 1, received ${snapshot.protocolVersion ?? "none"}.`);
    }
    this.snapshot = snapshot;
    this.status = snapshot.status;
    this.connected = true;
    this.error = null;
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
  }

  private scheduleReconnect() {
    if (!this.shouldReconnect || this.reconnectTimer) return;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      if (!this.shouldReconnect) return;
      void this.connect().catch(() => undefined);
    }, 750);
  }
}
