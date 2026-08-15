use std::{
    collections::HashMap,
    io::{BufRead, BufReader, Write},
    process::{Child, ChildStdin, Command, Stdio},
    str::FromStr,
    sync::{
        atomic::{AtomicBool, AtomicU64, Ordering},
        mpsc, Arc, Mutex,
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
    process: Arc<Mutex<ProcessState>>,
    pending: Arc<Mutex<PendingCommands>>,
    next_id: Arc<AtomicU64>,
    shortcuts_enabled: Arc<AtomicBool>,
    start_stop_shortcut: Arc<Mutex<Option<String>>>,
    lockpicking_start_stop_shortcut: Arc<Mutex<Option<String>>>,
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
    pub(crate) fn set_shortcuts_enabled(&self, enabled: bool) {
        self.shortcuts_enabled.store(enabled, Ordering::Relaxed);
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
    }

    fn resource_path(app: &AppHandle) -> Result<std::path::PathBuf, String> {
        app.path()
            .resource_dir()
            .map_err(|error| error.to_string())
            .map(|path| path.join("resources").join("engine").join("CuePilot.exe"))
    }

    fn ensure_started(&self, app: &AppHandle) -> Result<(), String> {
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
        let input = child.stdin.take().ok_or("Engine did not expose stdin.")?;
        let stdout = child.stdout.take().ok_or("Engine did not expose stdout.")?;
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
                    let _ = child.wait();
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
        // Closing the shell must never start an otherwise unused sidecar just to
        // deliver a shutdown request. Send the best-effort request only through
        // an already-open pipe, then close the owned process below.
        let request = serde_json::json!({
            "id": format!("shutdown-{}", self.next_id.fetch_add(1, Ordering::Relaxed)),
            "command": "shutdown",
            "processId": Value::Null,
            "settings": Value::Null,
        });
        if let Ok(mut process) = self.process.lock() {
            if let Some(stream) = process.input.as_mut() {
                let _ = writeln!(stream, "{request}");
                let _ = stream.flush();
            }
        }
        let child = self.process.lock().ok().and_then(|mut process| {
            process.input.take();
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
        if !self.shortcuts_enabled.load(Ordering::Relaxed) {
            return None;
        }
        if shortcut_matches(&self.start_stop_shortcut, "F10", shortcut) {
            Some("toggle")
        } else if shortcut_matches(&self.lockpicking_start_stop_shortcut, "F9", shortcut) {
            Some("toggle_lockpicking_class_c")
        } else if shortcut_matches(&self.emergency_shortcut, "Pause", shortcut) {
            Some("stop")
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
            &self.start_stop_shortcut,
            settings.get("startStop"),
            "F10",
            "Start / Stop",
        );
        sync_registered_shortcut(
            app,
            &self.lockpicking_start_stop_shortcut,
            settings.get("lockpickingStartStop"),
            "F9",
            "Lockpicking Start / Stop",
        );
        sync_registered_shortcut(
            app,
            &self.emergency_shortcut,
            settings.get("emergencyStop"),
            "Pause",
            "Emergency stop",
        );
    }
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
    fallback: &str,
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
    if registered.is_none() {
        match app.global_shortcut().register(shortcut.as_str()) {
            Ok(()) => *registered = Some(shortcut),
            Err(error) => emit_shortcut_warning(
                app,
                format!("{label} shortcut '{shortcut}' is already in use; CuePilot remains open without claiming it. {error}"),
            ),
        }
        return;
    }
    if registered.as_deref() == Some(shortcut.as_str()) {
        return;
    }

    let shortcuts = app.global_shortcut();
    let previous = registered.as_deref().unwrap_or(fallback).to_string();
    if let Err(error) = shortcuts.unregister(previous.as_str()) {
        emit_shortcut_warning(app, format!("Update {label} shortcut: {error}"));
        return;
    }
    match shortcuts.register(shortcut.as_str()) {
        Ok(()) => *registered = Some(shortcut),
        Err(error) => {
            let _ = shortcuts.register(previous.as_str());
            *registered = Some(previous.clone());
            emit_shortcut_warning(
                app,
                format!("{label} shortcut '{shortcut}' is unsupported; {previous} remains active. {error}"),
            );
        }
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
    let _ = app.emit(
        "engine://event",
        serde_json::json!({ "name": "fault", "payload": { "detail": detail } }),
    );
}

fn dispatch_engine_line(app: &AppHandle, pending: &Arc<Mutex<PendingCommands>>, line: &str) {
    match serde_json::from_str::<EngineMessage>(line) {
        Ok(EngineMessage::Response {
            id,
            ok,
            result,
            error,
        }) => {
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
            let _ = app.emit(
                "engine://event",
                serde_json::json!({ "name": name, "payload": payload }),
            );
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
    let _ = app.emit(
        "engine://event",
        serde_json::json!({
            "name": "bridge_state",
            "payload": { "connected": connected, "detail": detail }
        }),
    );
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
    }
}
