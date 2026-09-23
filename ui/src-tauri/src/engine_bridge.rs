use std::{
    collections::HashMap,
    io::{BufRead, BufReader, Write},
    process::{Child, ChildStdin, Command, Stdio},
    str::FromStr,
    sync::{
        atomic::{AtomicBool, AtomicU64, Ordering},
        mpsc, Arc, Mutex, MutexGuard,
    },
    thread,
    time::Duration,
};

use serde::Deserialize;
use serde_json::Value;
use tauri::{AppHandle, Emitter, Manager};
use tauri_plugin_global_shortcut::{GlobalShortcutExt, Shortcut};

#[cfg(windows)]
use std::os::windows::process::CommandExt;

const EXPECTED_PROTOCOL_VERSION: u64 = 1;
const DEFAULT_COMMAND_TIMEOUT: Duration = Duration::from_secs(5);
#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

type CommandResult = Result<Value, String>;
type PendingCommands = HashMap<String, mpsc::Sender<CommandResult>>;

#[derive(Default)]
struct ProcessState {
    child: Option<Child>,
    input: Option<ChildStdin>,
    generation: u64,
}

#[derive(Clone, Default)]
pub(crate) struct EngineBridge {
    lifecycle: Arc<Mutex<()>>,
    process: Arc<Mutex<ProcessState>>,
    pending: Arc<Mutex<PendingCommands>>,
    next_id: Arc<AtomicU64>,
    shortcuts_enabled: Arc<AtomicBool>,
    /// True while the Settings drawer is listening for a new binding: the
    /// keyboard shortcuts are released so the WebView can see them, and mouse
    /// shortcuts pass through to the page instead of firing commands.
    capture_suspended: Arc<AtomicBool>,
    start_stop_shortcut: Arc<Mutex<Option<String>>>,
    lockpicking_start_stop_shortcut: Arc<Mutex<Option<String>>>,
    pickpocket_start_stop_shortcut: Arc<Mutex<Option<String>>>,
    emergency_shortcut: Arc<Mutex<Option<String>>>,
}

#[derive(Debug, Deserialize)]
#[serde(tag = "type", rename_all = "lowercase")]
enum EngineMessage {
    Response {
        id: String,
        ok: bool,
        #[serde(default)]
        result: Option<Value>,
        #[serde(default)]
        error: Option<String>,
    },
    Event {
        name: String,
        payload: Value,
    },
}

impl EngineBridge {
    fn lifecycle_guard(&self) -> Result<MutexGuard<'_, ()>, String> {
        self.lifecycle
            .lock()
            .map_err(|_| "Engine lifecycle lock failed.".to_string())
    }

    pub(crate) fn set_shortcuts_enabled(&self, enabled: bool) {
        self.shortcuts_enabled.store(enabled, Ordering::Relaxed);
    }

    fn shortcut_slots(&self) -> [(&Mutex<Option<String>>, &'static str, &'static str); 4] {
        [
            (&self.start_stop_shortcut, "F10", "Start / Stop"),
            (
                &self.lockpicking_start_stop_shortcut,
                "F9",
                "Lockpicking Start / Stop",
            ),
            (&self.emergency_shortcut, "Pause", "Emergency stop"),
            (
                &self.pickpocket_start_stop_shortcut,
                "F7",
                "Pickpocket Start / Stop",
            ),
        ]
    }

    fn shortcuts_active(&self) -> bool {
        self.shortcuts_enabled.load(Ordering::Relaxed)
            && !self.capture_suspended.load(Ordering::Relaxed)
    }

    /// Releases (or re-claims) every registered keyboard shortcut while the UI
    /// captures a new binding. Windows delivers a registered hotkey only to the
    /// registrant, so without this the page could never observe the current key.
    pub(crate) fn set_capture_suspended(&self, app: &AppHandle, active: bool) {
        if !self.shortcuts_enabled.load(Ordering::Relaxed) {
            return;
        }
        if self.capture_suspended.swap(active, Ordering::Relaxed) == active {
            return;
        }
        let shortcuts = app.global_shortcut();
        for (slot, _, label) in self.shortcut_slots() {
            let Some(registered) = slot.lock().ok().and_then(|value| value.clone()) else {
                continue;
            };
            if is_mouse_shortcut(&registered) {
                continue;
            }
            let outcome = if active {
                shortcuts.unregister(registered.as_str())
            } else {
                shortcuts.register(registered.as_str())
            };
            if let Err(error) = outcome {
                let verb = if active { "release" } else { "re-claim" };
                emit_shortcut_warning(
                    app,
                    format!(
                        "Could not {verb} the {label} shortcut '{registered}' for capture. {error}"
                    ),
                );
            }
        }
    }

    /// The stored text for a command's binding, used to label the overlay toast.
    pub(crate) fn registered_shortcut(&self, command: &str) -> Option<String> {
        let (slot, fallback) = match command {
            "toggle" => (&self.start_stop_shortcut, "F10"),
            "toggle_lockpicking_class_c" => (&self.lockpicking_start_stop_shortcut, "F9"),
            "stop" => (&self.emergency_shortcut, "Pause"),
            "toggle_pickpocket_observe" => (&self.pickpocket_start_stop_shortcut, "F7"),
            _ => return None,
        };
        Some(
            slot.lock()
                .ok()
                .and_then(|value| value.clone())
                .unwrap_or_else(|| fallback.to_string()),
        )
    }

    /// Routes a mouse-button shortcut (`Ctrl+MouseX1`) the same way keyboard
    /// shortcuts are routed, using the same slot priority.
    pub(crate) fn command_for_mouse(&self, shortcut: &str) -> Option<&'static str> {
        if !self.shortcuts_active() {
            return None;
        }
        let matches = |slot: &Mutex<Option<String>>| {
            slot.lock()
                .ok()
                .and_then(|value| value.clone())
                .is_some_and(|configured| configured.eq_ignore_ascii_case(shortcut))
        };
        if matches(&self.start_stop_shortcut) {
            Some("toggle")
        } else if matches(&self.lockpicking_start_stop_shortcut) {
            Some("toggle_lockpicking_class_c")
        } else if matches(&self.emergency_shortcut) {
            Some("stop")
        } else if matches(&self.pickpocket_start_stop_shortcut) {
            Some("toggle_pickpocket_observe")
        } else {
            None
        }
    }

    pub(crate) fn register_default_shortcuts(&self, app: &AppHandle) {
        register_initial_shortcut(app, &self.start_stop_shortcut, "F10", "Start / Stop");
        register_initial_shortcut(
            app,
            &self.lockpicking_start_stop_shortcut,
            "F9",
            "Lockpicking Start / Stop",
        );
        register_initial_shortcut(app, &self.emergency_shortcut, "Pause", "Emergency stop");
        register_initial_shortcut(
            app,
            &self.pickpocket_start_stop_shortcut,
            "F7",
            "Pickpocket Start / Stop",
        );
    }

    fn resource_path(app: &AppHandle) -> Result<std::path::PathBuf, String> {
        app.path()
            .resource_dir()
            .map_err(|error| error.to_string())
            .map(|path| path.join("resources").join("engine").join("CuePilot.exe"))
    }

    fn ensure_started(&self, app: &AppHandle) -> Result<(), String> {
        // The guard spans the no-child check, spawn, pipe extraction, and state
        // publication. Concurrent commands can never both observe an empty
        // process slot and create duplicate sidecars.
        let _lifecycle = self.lifecycle_guard()?;
        let stale_child = {
            let mut process = self
                .process
                .lock()
                .map_err(|_| "Engine process lock failed.")?;
            let running = match process.child.as_mut() {
                Some(child) => child
                    .try_wait()
                    .map_err(|error| format!("Check local engine: {error}"))?
                    .is_none(),
                None => false,
            };
            if running && process.input.is_some() {
                return Ok(());
            }

            process.input.take();
            process.child.take()
        };
        if let Some(child) = stale_child {
            stop_owned_child(child);
        }

        let executable = Self::resource_path(app)?;
        if !executable.exists() {
            return Err(format!(
                "Engine sidecar is missing: {}",
                executable.display()
            ));
        }

        let mut command = Command::new(executable);
        #[cfg(windows)]
        command.creation_flags(CREATE_NO_WINDOW);
        let mut child = command
            .arg("--ui-bridge")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|error| format!("Start local engine: {error}"))?;
        let (Some(input), Some(stdout)) = (child.stdin.take(), child.stdout.take()) else {
            stop_owned_child(child);
            return Err("Engine did not expose its stdin/stdout pipes.".into());
        };
        let stderr = child.stderr.take();

        let generation = {
            let mut process = self
                .process
                .lock()
                .map_err(|_| "Engine process lock failed.")?;
            process.generation = process.generation.wrapping_add(1);
            let generation = process.generation;
            process.input = Some(input);
            process.child = Some(child);
            generation
        };

        emit_bridge_state(app, true, "Local engine started.");
        self.spawn_output_reader(app.clone(), stdout, generation);
        if let Some(stderr) = stderr {
            thread::spawn(move || {
                for line in BufReader::new(stderr).lines().map_while(Result::ok) {
                    crate::support::log("engine_stderr", &line);
                    eprintln!("[workflow-engine] {line}");
                }
            });
        }
        Ok(())
    }

    fn spawn_output_reader(
        &self,
        app: AppHandle,
        stdout: impl std::io::Read + Send + 'static,
        generation: u64,
    ) {
        let process = Arc::clone(&self.process);
        let pending = Arc::clone(&self.pending);
        thread::spawn(move || {
            let mut exit_detail = "Local engine closed its output stream.".to_string();
            for line in BufReader::new(stdout).lines() {
                match line {
                    Ok(line) => dispatch_engine_line(&app, &pending, &line),
                    Err(error) => {
                        exit_detail = format!("Read local engine output: {error}");
                        break;
                    }
                }
            }

            let (child, is_current) = match process.lock() {
                Ok(mut state) if state.generation == generation => {
                    state.input.take();
                    (state.child.take(), true)
                }
                _ => (None, false),
            };
            if !is_current {
                return;
            }
            if let Some(child) = child {
                thread::spawn(move || {
                    let mut child = child;
                    crate::support::log("engine_exit", &format!("{:?}", child.wait()));
                });
            }
            fail_pending(&pending, &exit_detail);
            emit_bridge_state(&app, false, &exit_detail);
        });
    }

    pub(crate) fn command(
        &self,
        app: &AppHandle,
        command: &str,
        target_process_id: Option<u32>,
        settings: Option<Value>,
    ) -> CommandResult {
        self.ensure_started(app)?;
        let id = format!("{command}-{}", self.next_id.fetch_add(1, Ordering::Relaxed));
        let request = serde_json::json!({
            "id": id,
            "command": command,
            "processId": target_process_id,
            "settings": settings,
        });
        let (sender, receiver) = mpsc::channel();
        self.pending
            .lock()
            .map_err(|_| "Engine response lock failed.")?
            .insert(id.clone(), sender);

        let send_result = (|| {
            let mut process = self
                .process
                .lock()
                .map_err(|_| "Engine process lock failed.")?;
            let stream = process
                .input
                .as_mut()
                .ok_or("Engine input is unavailable.")?;
            writeln!(stream, "{request}")
                .map_err(|error| format!("Send engine command: {error}"))?;
            stream
                .flush()
                .map_err(|error| format!("Flush engine command: {error}"))
        })();
        if let Err(error) = send_result {
            self.remove_pending(&id);
            if let Ok(mut process) = self.process.lock() {
                process.input.take();
            }
            return Err(error);
        }

        let result = match receiver.recv_timeout(DEFAULT_COMMAND_TIMEOUT) {
            Ok(result) => result?,
            Err(mpsc::RecvTimeoutError::Timeout) => {
                self.remove_pending(&id);
                crate::support::log("command_timeout", &format!("{id}: {command}"));
                return Err(format!("Local engine timed out while handling {command}."));
            }
            Err(mpsc::RecvTimeoutError::Disconnected) => {
                self.remove_pending(&id);
                return Err("Local engine disconnected before responding.".into());
            }
        };

        let protocol = result.get("protocolVersion").and_then(Value::as_u64);
        if protocol != Some(EXPECTED_PROTOCOL_VERSION) {
            return Err(format!(
                "Engine/UI protocol mismatch. Expected {EXPECTED_PROTOCOL_VERSION}, received {}. Rebuild the local engine sidecar.",
                protocol.map_or_else(|| "none".to_string(), |value| value.to_string())
            ));
        }
        self.sync_shortcuts(app, &result);
        Ok(result)
    }

    pub(crate) fn shutdown(&self, _app: &AppHandle) {
        let _lifecycle = match self.lifecycle_guard() {
            Ok(guard) => guard,
            Err(error) => {
                fail_pending(&self.pending, &error);
                return;
            }
        };
        // Closing the shell must never start an otherwise unused sidecar just to
        // deliver a shutdown request. Send the best-effort request only through
        // an already-open pipe, then close the owned process below.
        let request = serde_json::json!({
            "id": format!("shutdown-{}", self.next_id.fetch_add(1, Ordering::Relaxed)),
            "command": "shutdown",
            "processId": Value::Null,
            "settings": Value::Null,
        });
        let child = self.process.lock().ok().and_then(|mut process| {
            if let Some(stream) = process.input.as_mut() {
                let _ = writeln!(stream, "{request}");
                let _ = stream.flush();
            }
            process.input.take();
            process.generation = process.generation.wrapping_add(1);
            process.child.take()
        });
        if let Some(child) = child {
            stop_owned_child(child);
        }
        fail_pending(&self.pending, "Tauri shell closed.");
    }

    fn remove_pending(&self, id: &str) {
        if let Ok(mut pending) = self.pending.lock() {
            pending.remove(id);
        }
    }

    pub(crate) fn command_for_shortcut(&self, shortcut: &Shortcut) -> Option<&'static str> {
        if !self.shortcuts_active() {
            return None;
        }
        if shortcut_matches(&self.start_stop_shortcut, "F10", shortcut) {
            Some("toggle")
        } else if shortcut_matches(&self.lockpicking_start_stop_shortcut, "F9", shortcut) {
            Some("toggle_lockpicking_class_c")
        } else if shortcut_matches(&self.emergency_shortcut, "Pause", shortcut) {
            Some("stop")
        } else if shortcut_matches(&self.pickpocket_start_stop_shortcut, "F7", shortcut) {
            Some("toggle_pickpocket_observe")
        } else {
            None
        }
    }

    fn sync_shortcuts(&self, app: &AppHandle, snapshot: &Value) {
        if !self.shortcuts_enabled.load(Ordering::Relaxed) {
            return;
        }
        let Some(settings) = snapshot.get("settings") else {
            return;
        };
        sync_registered_shortcut(
            app,
            &self.pickpocket_start_stop_shortcut,
            settings.get("pickpocketStartStop"),
            "Pickpocket Start / Stop",
        );
        sync_registered_shortcut(
            app,
            &self.start_stop_shortcut,
            settings.get("startStop"),
            "Start / Stop",
        );
        sync_registered_shortcut(
            app,
            &self.lockpicking_start_stop_shortcut,
            settings.get("lockpickingStartStop"),
            "Lockpicking Start / Stop",
        );
        sync_registered_shortcut(
            app,
            &self.emergency_shortcut,
            settings.get("emergencyStop"),
            "Emergency stop",
        );
    }
}

/// Human label for a stored shortcut: `Ctrl+MouseX1` → `Ctrl + Mouse 4`,
/// `Shift+KeyG` → `Shift + G`. Mirrors `describeKey` in `ui/src/lib/hotkeys.ts`.
pub(crate) fn shortcut_label(shortcut: &str) -> String {
    shortcut
        .split('+')
        .map(|part| match part {
            "MouseX1" => "Mouse 4".to_string(),
            "MouseX2" => "Mouse 5".to_string(),
            "MouseMiddle" => "Middle Mouse".to_string(),
            "Pause" => "Pause / Break".to_string(),
            "Return" => "Enter".to_string(),
            key if key.len() == 4 && key.starts_with("Key") => key[3..].to_string(),
            key if key.len() == 6 && key.starts_with("Digit") => key[5..].to_string(),
            key if key.len() == 7 && key.starts_with("Numpad") => format!("Num {}", &key[6..]),
            key => key.to_string(),
        })
        .collect::<Vec<_>>()
        .join(" + ")
}

/// Mouse bindings are stored as `[Ctrl+][Shift+][Alt+]Mouse{Middle,X1,X2}` and are
/// served by the low-level mouse hook, never by the global-shortcut plugin.
pub(crate) fn is_mouse_shortcut(shortcut: &str) -> bool {
    shortcut
        .rsplit('+')
        .next()
        .is_some_and(|key| key.len() > 5 && key[..5].eq_ignore_ascii_case("Mouse"))
}

fn register_initial_shortcut(
    app: &AppHandle,
    current: &Mutex<Option<String>>,
    shortcut: &str,
    label: &str,
) {
    match app.global_shortcut().register(shortcut) {
        Ok(()) => {
            if let Ok(mut registered) = current.lock() {
                *registered = Some(shortcut.to_string());
            }
        }
        Err(error) => emit_shortcut_warning(
            app,
            format!("{label} shortcut '{shortcut}' is already in use; CuePilot remains open without claiming it. {error}"),
        ),
    }
}

fn shortcut_matches(current: &Mutex<Option<String>>, fallback: &str, shortcut: &Shortcut) -> bool {
    let configured = current
        .lock()
        .ok()
        .and_then(|value| value.clone())
        .unwrap_or_else(|| fallback.to_string());
    Shortcut::from_str(&configured)
        .map(|candidate| candidate.id() == shortcut.id())
        .unwrap_or(false)
}

fn sync_registered_shortcut(
    app: &AppHandle,
    current: &Mutex<Option<String>>,
    binding: Option<&Value>,
    label: &str,
) {
    let Some(binding) = binding else {
        return;
    };
    let Some(key) = binding.get("key").and_then(Value::as_str) else {
        return;
    };
    let shortcut = shortcut_string(binding, key);
    let Ok(mut registered) = current.lock() else {
        return;
    };
    if registered.as_deref() == Some(shortcut.as_str()) {
        return;
    }

    let shortcuts = app.global_shortcut();
    let previous = registered.clone();
    // Only keyboard bindings live in the plugin; a mouse binding just changes the
    // text the hook compares against.
    if let Some(previous) = previous
        .as_deref()
        .filter(|value| !is_mouse_shortcut(value))
    {
        if let Err(error) = shortcuts.unregister(previous) {
            emit_shortcut_warning(app, format!("Update {label} shortcut: {error}"));
            return;
        }
    }
    if is_mouse_shortcut(&shortcut) {
        *registered = Some(shortcut);
        return;
    }
    match shortcuts.register(shortcut.as_str()) {
        Ok(()) => *registered = Some(shortcut),
        Err(error) => match previous {
            Some(previous) => {
                if !is_mouse_shortcut(&previous) {
                    let _ = shortcuts.register(previous.as_str());
                }
                *registered = Some(previous.clone());
                emit_shortcut_warning(
                    app,
                    format!("{label} shortcut '{shortcut}' is unsupported; {previous} remains active. {error}"),
                );
            }
            None => emit_shortcut_warning(
                app,
                format!("{label} shortcut '{shortcut}' is already in use; CuePilot remains open without claiming it. {error}"),
            ),
        },
    }
}

fn shortcut_string(binding: &Value, key: &str) -> String {
    let mut parts = Vec::new();
    if binding.get("control").and_then(Value::as_bool) == Some(true) {
        parts.push("Ctrl".to_string());
    }
    if binding.get("shift").and_then(Value::as_bool) == Some(true) {
        parts.push("Shift".to_string());
    }
    if binding.get("alt").and_then(Value::as_bool) == Some(true) {
        parts.push("Alt".to_string());
    }
    let normalized = if key.len() == 2 && key.starts_with('D') && key.as_bytes()[1].is_ascii_digit()
    {
        key[1..].to_string()
    } else if key.eq_ignore_ascii_case("Return") {
        "Enter".to_string()
    } else {
        key.to_string()
    };
    parts.push(normalized);
    parts.join("+")
}

fn emit_shortcut_warning(app: &AppHandle, detail: String) {
    let payload = serde_json::json!({ "name": "fault", "payload": { "detail": detail } });
    crate::publish_overlay(app, payload.clone());
    let _ = app.emit("engine://event", payload);
}

fn dispatch_engine_line(app: &AppHandle, pending: &Arc<Mutex<PendingCommands>>, line: &str) {
    match serde_json::from_str::<EngineMessage>(line) {
        Ok(EngineMessage::Response {
            id,
            ok,
            result,
            error,
        }) => {
            if ok {
                if let Some(snapshot) = result
                    .as_ref()
                    .filter(|value| value.get("protocolVersion").is_some())
                {
                    crate::notifications::consume_snapshot(app, snapshot);
                }
            }
            let sender = pending
                .lock()
                .ok()
                .and_then(|mut values| values.remove(&id));
            if let Some(sender) = sender {
                let response = if ok {
                    Ok(result.unwrap_or(Value::Null))
                } else {
                    Err(error.unwrap_or_else(|| "Local engine rejected the command.".into()))
                };
                let _ = sender.send(response);
            }
        }
        Ok(EngineMessage::Event { name, payload }) => {
            let overlay_payload = serde_json::json!({ "name": name, "payload": payload });
            crate::publish_overlay(app, overlay_payload.clone());
            let _ = app.emit("engine://event", overlay_payload);
        }
        Err(error) => {
            let _ = app.emit(
                "engine://event",
                serde_json::json!({
                    "name": "fault",
                    "payload": { "detail": format!("Engine returned invalid JSON: {error}") }
                }),
            );
        }
    }
}

fn fail_pending(pending: &Arc<Mutex<PendingCommands>>, detail: &str) {
    if let Ok(mut commands) = pending.lock() {
        for (_, sender) in commands.drain() {
            let _ = sender.send(Err(detail.to_string()));
        }
    }
}

fn emit_bridge_state(app: &AppHandle, connected: bool, detail: &str) {
    let payload = serde_json::json!({
        "name": "bridge_state",
        "payload": { "connected": connected, "detail": detail }
    });
    crate::publish_overlay(app, payload.clone());
    let _ = app.emit("engine://event", payload);
}

fn stop_owned_child(mut child: Child) {
    for _ in 0..20 {
        match child.try_wait() {
            Ok(Some(_)) => return,
            Ok(None) => thread::sleep(Duration::from_millis(25)),
            Err(_) => return,
        }
    }
    let _ = child.kill();
    let _ = child.wait();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cloned_bridges_share_one_lifecycle_gate() {
        let bridge = EngineBridge::default();
        let clone = bridge.clone();
        let first = bridge.lifecycle_guard().expect("first lifecycle guard");
        let (sender, receiver) = mpsc::channel();

        let waiter = thread::spawn(move || {
            let _second = clone.lifecycle_guard().expect("second lifecycle guard");
            sender.send(()).expect("report acquired lifecycle guard");
        });

        assert!(receiver.recv_timeout(Duration::from_millis(40)).is_err());
        drop(first);
        receiver
            .recv_timeout(Duration::from_secs(1))
            .expect("second bridge should enter after the first exits");
        waiter.join().expect("lifecycle waiter should exit");
    }

    #[test]
    fn decodes_success_response() {
        let message: EngineMessage = serde_json::from_str(
            r#"{"type":"response","id":"snapshot-1","ok":true,"result":{"protocolVersion":1}}"#,
        )
        .expect("response should decode");

        match message {
            EngineMessage::Response { id, ok, result, .. } => {
                assert_eq!(id, "snapshot-1");
                assert!(ok);
                assert_eq!(result.unwrap()["protocolVersion"], 1);
            }
            _ => panic!("expected response"),
        }
    }

    #[test]
    fn decodes_correlated_error() {
        let message: EngineMessage = serde_json::from_str(
            r#"{"type":"response","id":"start-2","ok":false,"result":null,"error":"No FiveM target."}"#,
        )
        .expect("error response should decode");

        match message {
            EngineMessage::Response { id, ok, error, .. } => {
                assert_eq!(id, "start-2");
                assert!(!ok);
                assert_eq!(error.as_deref(), Some("No FiveM target."));
            }
            _ => panic!("expected response"),
        }
    }

    #[test]
    fn decodes_event_separately_from_response() {
        let message: EngineMessage = serde_json::from_str(
            r#"{"type":"event","name":"status","payload":{"state":"Stopped"}}"#,
        )
        .expect("event should decode");

        match message {
            EngineMessage::Event { name, payload } => {
                assert_eq!(name, "status");
                assert_eq!(payload["state"], "Stopped");
            }
            _ => panic!("expected event"),
        }
    }

    #[test]
    fn converts_legacy_shortcut_shape() {
        let binding = serde_json::json!({
            "key": "D1",
            "control": true,
            "shift": true,
            "alt": false
        });
        assert_eq!(shortcut_string(&binding, "D1"), "Ctrl+Shift+1");
        assert_eq!(shortcut_string(&serde_json::json!({}), "Pause"), "Pause");
    }

    #[test]
    fn routes_configured_toggle_and_emergency_shortcuts() {
        let bridge = EngineBridge::default();
        bridge.set_shortcuts_enabled(true);
        *bridge.start_stop_shortcut.lock().unwrap() = Some("F11".into());
        *bridge.lockpicking_start_stop_shortcut.lock().unwrap() = Some("F9".into());
        *bridge.emergency_shortcut.lock().unwrap() = Some("Pause".into());

        let start_stop = Shortcut::from_str("F11").unwrap();
        let lockpicking_start_stop = Shortcut::from_str("F9").unwrap();
        let emergency = Shortcut::from_str("Pause").unwrap();
        let unrelated = Shortcut::from_str("F10").unwrap();

        assert_eq!(bridge.command_for_shortcut(&start_stop), Some("toggle"));
        assert_eq!(
            bridge.command_for_shortcut(&lockpicking_start_stop),
            Some("toggle_lockpicking_class_c")
        );
        assert_eq!(bridge.command_for_shortcut(&emergency), Some("stop"));
        assert_eq!(bridge.command_for_shortcut(&unrelated), None);
    }

    #[test]
    fn disabled_profile_never_routes_global_shortcuts() {
        let bridge = EngineBridge::default();
        bridge.set_shortcuts_enabled(false);
        let start_stop = Shortcut::from_str("F10").unwrap();
        let lockpicking_start_stop = Shortcut::from_str("F9").unwrap();
        let emergency = Shortcut::from_str("Pause").unwrap();

        assert_eq!(bridge.command_for_shortcut(&start_stop), None);
        assert_eq!(bridge.command_for_shortcut(&lockpicking_start_stop), None);
        assert_eq!(bridge.command_for_shortcut(&emergency), None);
        assert_eq!(
            bridge.command_for_shortcut(&Shortcut::from_str("F7").unwrap()),
            None
        );
    }

    #[test]
    fn labels_shortcuts_for_people() {
        assert_eq!(shortcut_label("F7"), "F7");
        assert_eq!(shortcut_label("Ctrl+MouseX1"), "Ctrl + Mouse 4");
        assert_eq!(shortcut_label("Shift+KeyG"), "Shift + G");
        assert_eq!(shortcut_label("Digit1"), "1");
        assert_eq!(shortcut_label("Numpad7"), "Num 7");
        assert_eq!(shortcut_label("Pause"), "Pause / Break");
    }

    #[test]
    fn recognises_mouse_shortcut_text() {
        assert!(is_mouse_shortcut("MouseX1"));
        assert!(is_mouse_shortcut("Ctrl+Shift+MouseMiddle"));
        assert!(is_mouse_shortcut("mousex2"));
        assert!(!is_mouse_shortcut("F10"));
        assert!(!is_mouse_shortcut("Ctrl+M"));
        assert!(!is_mouse_shortcut("Mouse"));
        assert_eq!(
            shortcut_string(&serde_json::json!({ "control": true }), "MouseX2"),
            "Ctrl+MouseX2"
        );
    }

    #[test]
    fn routes_mouse_shortcuts_through_the_same_slots() {
        let bridge = EngineBridge::default();
        bridge.set_shortcuts_enabled(true);
        *bridge.start_stop_shortcut.lock().unwrap() = Some("MouseX1".into());
        *bridge.pickpocket_start_stop_shortcut.lock().unwrap() = Some("Ctrl+MouseX2".into());

        assert_eq!(bridge.command_for_mouse("MouseX1"), Some("toggle"));
        assert_eq!(bridge.command_for_mouse("mousex1"), Some("toggle"));
        assert_eq!(
            bridge.command_for_mouse("Ctrl+MouseX2"),
            Some("toggle_pickpocket_observe")
        );
        assert_eq!(bridge.command_for_mouse("MouseX2"), None);
        assert_eq!(bridge.command_for_mouse("MouseMiddle"), None);
        assert_eq!(
            bridge.registered_shortcut("toggle").as_deref(),
            Some("MouseX1")
        );
        assert_eq!(bridge.registered_shortcut("stop").as_deref(), Some("Pause"));
    }

    #[test]
    fn capture_suspends_routing_for_keyboard_and_mouse() {
        let bridge = EngineBridge::default();
        bridge.set_shortcuts_enabled(true);
        *bridge.start_stop_shortcut.lock().unwrap() = Some("MouseX1".into());
        bridge.capture_suspended.store(true, Ordering::Relaxed);

        assert_eq!(bridge.command_for_mouse("MouseX1"), None);
        assert_eq!(
            bridge.command_for_shortcut(&Shortcut::from_str("F7").unwrap()),
            None
        );

        bridge.capture_suspended.store(false, Ordering::Relaxed);
        assert_eq!(bridge.command_for_mouse("MouseX1"), Some("toggle"));
        assert_eq!(
            bridge.command_for_shortcut(&Shortcut::from_str("F7").unwrap()),
            Some("toggle_pickpocket_observe")
        );
    }

    #[test]
    fn pickpocket_toggle_uses_configured_binding_and_leaves_f8_unbound() {
        let bridge = EngineBridge::default();
        bridge.set_shortcuts_enabled(true);
        assert_eq!(
            bridge.command_for_shortcut(&Shortcut::from_str("F7").unwrap()),
            Some("toggle_pickpocket_observe")
        );
        assert_eq!(
            bridge.command_for_shortcut(&Shortcut::from_str("F8").unwrap()),
            None
        );
        *bridge.pickpocket_start_stop_shortcut.lock().unwrap() = Some("F6".into());
        assert_eq!(
            bridge.command_for_shortcut(&Shortcut::from_str("F6").unwrap()),
            Some("toggle_pickpocket_observe")
        );
        assert_eq!(
            bridge.command_for_shortcut(&Shortcut::from_str("F7").unwrap()),
            None
        );
    }

    #[test]
    fn shared_bridge_fixture_decodes_at_the_expected_protocol() {
        let line = include_str!("../../../tests/contracts/bridge-response-v1.json");
        match serde_json::from_str::<EngineMessage>(line)
            .expect("shared bridge fixture should decode")
        {
            EngineMessage::Response {
                result: Some(result),
                ..
            } => assert_eq!(result["protocolVersion"], EXPECTED_PROTOCOL_VERSION),
            other => panic!("unexpected shared fixture message: {other:?}"),
        }
    }
}
