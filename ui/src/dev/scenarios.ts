import { mockIPC, mockWindows } from "@tauri-apps/api/mocks";
import type { Snapshot, PickpocketRecentAttempt } from "../lib/engine.svelte";
import { defaultItemPriority, defaultPriority } from "../lib/activities/pickpocket-catalog";

export function installScenario(name: string) {
  if (!import.meta.env.DEV) throw new Error("Scenarios are development-only.");
  mockWindows("main");
  const hotkey = (key: string) => ({ key, control: false, alt: false, shift: false });
  const attempts: PickpocketRecentAttempt[] = Array.from({ length: 12 }, (_, index) => ({
    id: `fixture-${index}`, endedAtUnixMs: 1788494400000 - index * 200000, outcome: index % 3 === 0 ? "Grabbed" : index % 3 === 1 ? "Missed" : "Ended",
    color: "Yellow", widthPixels: 4, offsetPixels: index % 3 === 2 ? null : -5, automaticPresses: 1,
    itemName: index % 3 === 2 ? null : "TNT Recipe", redAdvanceMs: 8, yellowAdvanceMs: index === 0 ? 17 : 20,
    engineVersion: "5.3.1", sessionId: `fixture-${index}`, inputMode: "PrecisionAttempt", targetPolicy: "RarestFirst"
  }));
  let snapshot: Snapshot = {
    protocolVersion: 1, engineVersion: "5.3.1-scenario", routineState: "Stopped", status: { state: "Stopped", detail: "Scenario ready", sampleCount: 0, confidence: 0, debug: null },
    targetValid: name !== "missing-target", canStart: true, targetValidation: "Fixture window", targets: [], diagnosticsDirectory: "Scenario evidence", debug: null, setupVerification: null,
    settings: { formatVersion: 9, selectedProfile: "fishing", startStop: hotkey("F10"), lockpickingStartStop: hotkey("F9"), pickpocketStartStop: hotkey("F7"), emergencyStop: hotkey("Pause"),
      routine: { fishingLowerTensionPercent:55, fishingUpperTensionPercent:68, fishingSampleMilliseconds:40, fishingMinimumPulseMilliseconds:35, fishingMaximumPulseMilliseconds:90, fishingMinimumRestMilliseconds:70, fishingCastAccelerationDelayMilliseconds:5000, maximumDurationSeconds:210, collectDelayMilliseconds:250, collectOnTimeout:false, inputMode:"Automatic", targetWindow:{ processId:123, processName:"FiveM", windowTitle:"Fixture FiveM" } } },
    lockpicking: { observing:false, state:"Stopped", detail:"Observation ready", sampleCount:0, confidence:0, captureBackend:"None", captureMilliseconds:0, accumulatedFrames:0, spin:null, inputEnabled:false, vehicleClass:"", actionCount:0, spinInputActive:false, evidenceDirectory:"", observation:{ state:"Hidden", confidence:0, hudCenterX:0, hudCenterY:0, hudRadius:0, target:null, visibleTargetCount:0, predictedAction:"WAIT", reason:"No HUD" } },
    pickpocket: { observing:name === "running", state:name === "cooldown" ? "Cooldown" : "Stopped", detail:"Fixture session. No native input is connected.", sampleCount:200, captureMilliseconds:1, analysisMilliseconds:1, frameAgeMilliseconds:1, captureBackend:"Fixture", selectedBandIndex:null, targetPolicy:"RarestFirst", inputMode:"PrecisionAttempt", redAdvanceMs:8, yellowAdvanceMs:17, customPriority:[...defaultPriority], itemPriority:[...defaultItemPriority], predictedPressCount:1, cooldownUntilUnixMs:name === "cooldown" ? Date.now()+180000 : 0, evidenceDirectory:"", attempt:1, prediction:null, recentAttempts:attempts.slice(0,5), sessionStateError:name === "storage-error" ? "Fixture: history storage is unavailable." : null, observation:{ state:"Hidden", bar:{x:0,y:0,width:0,height:0}, markerX:0, bands:[], confidence:0, reason:"Fixture" } }
  };
  if (snapshot.pickpocket && ["armed", "tap-sent"].includes(name)) {
    snapshot.pickpocket.observing = true;
    snapshot.pickpocket.inputArmed = name === "armed";
    snapshot.pickpocket.automatedPressCount = name === "tap-sent" ? 1 : 0;
  }
  mockIPC(async (command, args) => {
    const payload = (args ?? {}) as Record<string, unknown>;
    if (command === "engine_command") {
      if (name === "disconnected") throw new Error("Fixture engine disconnected.");
      const action = payload.command;
      if (action === "pickpocket_history") {
        const settings = payload.settings as { page:number; outcome:string };
        const filtered = attempts.filter(attempt => settings.outcome === "All" || attempt.outcome === settings.outcome);
        const page = Math.min(settings.page, Math.max(0, Math.ceil(filtered.length/5)-1));
        return { protocolVersion:1, attempts:filtered.slice(page*5,page*5+5), total:filtered.length, page, pageSize:5, retention:1000, error:name === "storage-error" ? "Fixture storage error" : null };
      }
      if (action === "configure_pickpocket") {
        if (name === "save-error") throw new Error("Fixture: save failed.");
        if (name === "saving") await new Promise(resolve => setTimeout(resolve, 1500));
        snapshot = { ...snapshot, pickpocket: { ...snapshot.pickpocket!, ...(payload.settings as object) } };
      } else if (action === "save_settings") { snapshot = { ...snapshot, settings: payload.settings as Snapshot["settings"] }; }
      else if (!["snapshot", "list_targets", "select_target", "stop"].includes(String(action))) throw new Error(`Scenario blocks command: ${action}`);
      return JSON.parse(JSON.stringify(snapshot));
    }
    if (command === "updater_status") return { installed:name === "update-error", development:true, currentVersion:"5.3.1", detail:"Isolated scenario" };
    if (command === "check_for_updates") throw new Error("Fixture: update feed unavailable.");
    if (command === "diagnostics_snapshot") return {recentSamples:[],latestSample:null,debugSession:null};
    if (command === "support_health") return {shellVersion:"5.3.1",development:true,processId:0,diagnosticsAvailable:true,logBytes:0,logLimited:false,protocolVersion:1};
    if (command === "support_sessions") return {sessions:[],total:0,page:0,pageSize:5,totalBytes:0,storageLimited:false};
    if (command === "support_log") return null;
    if (command.startsWith("plugin:")) return null;
    throw new Error(`Scenario blocks native command: ${command}`);
  }, { shouldMockEvents: true });
  document.documentElement.dataset.scenario = name;
}
