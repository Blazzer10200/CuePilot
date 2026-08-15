use std::{fs, process::Command, thread};

use base64::{engine::general_purpose::STANDARD as BASE64, Engine as _};
use tauri::{AppHandle, Manager, State};

mod engine_bridge;

use engine_bridge::EngineBridge;

#[tauri::command]
fn engine_command(
    app: AppHandle,
    bridge: State<'_, EngineBridge>,
    command: String,
    target_process_id: Option<u32>,
    settings: Option<serde_json::Value>,
) -> Result<serde_json::Value, String> {
    match command.as_str() {
        "snapshot"
        | "start"
        | "stop"
        | "start_lockpicking_observe"
        | "start_lockpicking_class_c"
        | "toggle_lockpicking_class_c"
        | "stop_lockpicking_observe"
        | "list_targets" => bridge.command(&app, &command, None, None),
        "select_target" if target_process_id.is_some() => {
            bridge.command(&app, &command, target_process_id, None)
        }
        "select_target" => Err("Target process ID is required.".into()),
        "save_settings" if settings.is_some() => bridge.command(&app, &command, None, settings),
        "save_settings" => Err("Settings payload is required.".into()),
        _ => Err("Unsupported local engine command.".into()),
    }
}

fn diagnostics_directory() -> Result<std::path::PathBuf, String> {
    std::env::var("LOCALAPPDATA")
        .map_err(|_| "LOCALAPPDATA is unavailable.".to_string())
        .map(std::path::PathBuf::from)
        .map(|root| root.join("CuePilot").join("diagnostics"))
}

#[tauri::command]
fn diagnostics_snapshot() -> Result<serde_json::Value, String> {
    let directory = diagnostics_directory()?;
    fs::create_dir_all(&directory)
        .map_err(|error| format!("Create diagnostics directory: {error}"))?;

    let recent_samples = fs::read_to_string(directory.join("last-fishing.csv"))
        .unwrap_or_default()
        .lines()
        .rev()
        .take(60)
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .map(str::to_owned)
        .collect::<Vec<_>>();

    let latest_sample = fs::read_dir(&directory)
        .map_err(|error| format!("Read diagnostics directory: {error}"))?
        .filter_map(Result::ok)
        .filter(|entry| {
            entry
                .file_type()
                .map(|kind| kind.is_file())
                .unwrap_or(false)
                && {
                    let name = entry.file_name();
                    let name = name.to_string_lossy();
                    name.starts_with("meter-loss-") || name.starts_with("meter-lock-")
                }
                && entry
                    .path()
                    .extension()
                    .is_some_and(|extension| extension.eq_ignore_ascii_case("png"))
        })
        .max_by_key(|entry| {
            entry
                .metadata()
                .and_then(|metadata| metadata.modified())
                .ok()
        })
        .map(|entry| {
            let name = entry.file_name().to_string_lossy().to_string();
            let path = entry.path();
            let image_data = fs::read(&path)
                .ok()
                .filter(|bytes| bytes.len() <= 6 * 1024 * 1024)
                .map(|bytes| format!("data:image/png;base64,{}", BASE64.encode(bytes)));
            let metadata = fs::read_to_string(path.with_extension("json"))
                .ok()
                .and_then(|json| serde_json::from_str::<serde_json::Value>(&json).ok());
            serde_json::json!({ "name": name, "imageData": image_data, "metadata": metadata })
        });

    let debug_session = latest_debug_session(&directory);

    Ok(serde_json::json!({
        "directory": directory.display().to_string(),
        "recentSamples": recent_samples,
        "latestSample": latest_sample,
        "debugSession": debug_session,
    }))
}

fn latest_debug_session(diagnostics: &std::path::Path) -> Option<serde_json::Value> {
    let session = fs::read_dir(diagnostics.join("sessions"))
        .ok()?
        .filter_map(Result::ok)
        .filter(|entry| entry.file_type().map(|kind| kind.is_dir()).unwrap_or(false))
        .max_by_key(|entry| entry.file_name())?;
    let directory = session.path();
    let manifest_bytes = fs::read(directory.join("session.json")).ok()?;
    if manifest_bytes.len() > 512 * 1024 {
        return None;
    }
    let manifest = serde_json::from_slice::<serde_json::Value>(&manifest_bytes).ok()?;
    let recent_events = fs::read_to_string(directory.join("events.jsonl"))
        .unwrap_or_default()
        .lines()
        .rev()
        .take(120)
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .filter_map(|line| serde_json::from_str::<serde_json::Value>(line).ok())
        .collect::<Vec<_>>();
    let frames = manifest
        .get("frames")
        .and_then(serde_json::Value::as_array)
        .into_iter()
        .flatten()
        .filter_map(|frame| {
            let image_name = frame.get("imageName")?.as_str()?;
            let metadata_name = frame.get("metadataName")?.as_str()?;
            if image_name.contains('/')
                || image_name.contains('\\')
                || metadata_name.contains('/')
                || metadata_name.contains('\\')
            {
                return None;
            }
            let image_bytes = fs::read(directory.join(image_name)).ok()?;
            if image_bytes.len() > 8 * 1024 * 1024 {
                return None;
            }
            let metadata = fs::read_to_string(directory.join(metadata_name))
                .ok()
                .and_then(|json| serde_json::from_str::<serde_json::Value>(&json).ok());
            Some(serde_json::json!({
                "label": frame.get("label"),
                "score": frame.get("score"),
                "elapsedMilliseconds": frame.get("elapsedMilliseconds"),
                "imageName": image_name,
                "imageData": format!("data:image/png;base64,{}", BASE64.encode(image_bytes)),
                "metadata": metadata,
            }))
        })
        .collect::<Vec<_>>();

    Some(serde_json::json!({
        "directory": directory.display().to_string(),
        "manifest": manifest,
        "recentEvents": recent_events,
        "frames": frames,
    }))
}

#[tauri::command]
fn open_diagnostics() -> Result<(), String> {
    let directory = diagnostics_directory()?;
    fs::create_dir_all(&directory)
        .map_err(|error| format!("Create diagnostics directory: {error}"))?;
    Command::new("explorer.exe")
        .arg(&directory)
        .spawn()
        .map_err(|error| format!("Open diagnostics directory: {error}"))?;
    Ok(())
}

pub fn run() {
    tauri::Builder::default()
        .manage(EngineBridge::default())
        .setup(|app| {
            use tauri::Emitter;
            use tauri_plugin_global_shortcut::ShortcutState;

            app.handle().plugin(
                tauri_plugin_global_shortcut::Builder::new()
                    .with_handler(move |handle, shortcut, event| {
                        if event.state() == ShortcutState::Pressed {
                            let bridge = handle.state::<EngineBridge>().inner().clone();
                            let Some(command) = bridge.command_for_shortcut(shortcut) else {
                                return;
                            };
                            let handle = handle.clone();
                            thread::spawn(move || {
                                if let Err(detail) = bridge.command(&handle, command, None, None) {
                                    let _ = handle.emit(
                                        "engine://event",
                                        serde_json::json!({
                                            "name": "fault",
                                            "payload": { "detail": detail },
                                        }),
                                    );
                                }
                            });
                        }
                    })
                    .build(),
            )?;
            let owns_global_shortcuts = shortcut_profile_owns_hotkeys(&app.config().identifier);
            let bridge = app.state::<EngineBridge>().inner().clone();
            bridge.set_shortcuts_enabled(owns_global_shortcuts);
            if owns_global_shortcuts {
                bridge.register_default_shortcuts(app.handle());
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            engine_command,
            diagnostics_snapshot,
            open_diagnostics
        ])
        .on_window_event(|window, event| {
            if matches!(event, tauri::WindowEvent::Destroyed) {
                let bridge = window.state::<EngineBridge>();
                bridge.shutdown(window.app_handle());
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running CuePilot Tauri shell");
}

fn shortcut_profile_owns_hotkeys(identifier: &str) -> bool {
    !identifier.eq_ignore_ascii_case("com.blazzer.cuepilot.dev")
}

#[cfg(test)]
mod diagnostics_tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn official_profile_owns_hotkeys_while_development_profile_stays_passive() {
        assert!(shortcut_profile_owns_hotkeys("com.blazzer.cuepilot"));
        assert!(!shortcut_profile_owns_hotkeys("com.blazzer.cuepilot.dev"));
    }

    #[test]
    fn latest_debug_session_returns_manifest_and_recent_events() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("clock should be valid")
            .as_nanos();
        let root = std::env::temp_dir().join(format!("cuepilot-rust-test-{unique}"));
        let session = root.join("sessions").join("20260813-220000-test");
        fs::create_dir_all(&session).expect("session directory should be created");
        fs::write(
            session.join("session.json"),
            r#"{"sessionId":"test","active":false,"frames":[]}"#,
        )
        .expect("manifest should be written");
        fs::write(
            session.join("events.jsonl"),
            "{\"sequence\":1,\"eventName\":\"start\"}\n",
        )
        .expect("events should be written");

        let snapshot = latest_debug_session(&root).expect("debug session should load");

        assert_eq!(snapshot["manifest"]["sessionId"], "test");
        assert_eq!(snapshot["recentEvents"][0]["eventName"], "start");
        fs::remove_dir_all(root).expect("temporary diagnostics should be removed");
    }
}
