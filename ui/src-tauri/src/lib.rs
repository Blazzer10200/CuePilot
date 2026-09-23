use std::{
    fs,
    io::{Read, Seek, SeekFrom},
    process::Command,
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, OnceLock,
    },
    thread,
    time::Instant,
};

use base64::{engine::general_purpose::STANDARD as BASE64, Engine as _};
use tauri::{AppHandle, Manager, State};

mod engine_bridge;
#[cfg(windows)]
mod mouse_shortcuts;
mod notifications;
#[cfg(windows)]
mod splash;
mod support;
mod update_service;

use engine_bridge::EngineBridge;
use update_service::UpdateService;

// Keep a diagnostics refresh responsive even when an instrumented session has
// many full-resolution screenshots. The source files remain local and are
// always available through the Diagnostics folder.
const MAX_DEBUG_IMAGE_BYTES: usize = 12 * 1024 * 1024;
const MAX_SINGLE_DEBUG_IMAGE_BYTES: usize = 6 * 1024 * 1024;
const MAX_RECENT_DIAGNOSTIC_TEXT_BYTES: u64 = 256 * 1024;

/// Wall clock for launch phases. Set once at the top of `run()` so every phase
/// is measured from the same instant the user double-clicked the shortcut.
static LAUNCHED_AT: OnceLock<Instant> = OnceLock::new();
static FIRST_UI_COMMAND_RECORDED: AtomicBool = AtomicBool::new(false);

fn launch_elapsed_ms() -> u64 {
    LAUNCHED_AT
        .get()
        .map(|start| start.elapsed().as_millis() as u64)
        .unwrap_or_default()
}

/// Appends one bounded line per launch phase to the existing shell log. This is
/// how a slow cold start is attributed without attaching a debugger to a release
/// build, which has no CDP.
pub(crate) fn record_startup_phase(phase: &str) {
    support::log("startup", &format!("{phase} at {}ms", launch_elapsed_ms()));
}

fn read_limited(reader: impl Read, byte_limit: usize) -> Option<Vec<u8>> {
    let mut bytes = Vec::new();
    reader
        .take((byte_limit as u64).saturating_add(1))
        .read_to_end(&mut bytes)
        .ok()?;
    (bytes.len() <= byte_limit).then_some(bytes)
}

fn read_tail_lines(path: &std::path::Path, byte_limit: u64, line_limit: usize) -> Vec<String> {
    let Ok(mut file) = fs::File::open(path) else {
        return vec![];
    };
    let Ok(length) = file.metadata().map(|metadata| metadata.len()) else {
        return vec![];
    };
    let truncated = length > byte_limit;
    if file
        .seek(SeekFrom::Start(length.saturating_sub(byte_limit)))
        .is_err()
    {
        return vec![];
    }
    let mut reader = file.take(byte_limit.saturating_add(1));
    let mut bytes = Vec::new();
    if reader.read_to_end(&mut bytes).is_err() {
        return vec![];
    }
    let truncated = truncated || bytes.len() > byte_limit as usize;
    bytes.truncate(byte_limit as usize);
    let text = String::from_utf8_lossy(&bytes);
    let complete_lines = if truncated {
        text.split_once('\n').map_or("", |(_, rest)| rest)
    } else {
        &text
    };
    complete_lines
        .lines()
        .rev()
        .take(line_limit)
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .map(str::to_owned)
        .collect()
}

pub(crate) fn publish_overlay(app: &AppHandle, payload: serde_json::Value) {
    notifications::consume(app, &payload);
}

#[tauri::command]
fn engine_command(
    app: AppHandle,
    bridge: State<'_, EngineBridge>,
    command: String,
    target_process_id: Option<u32>,
    settings: Option<serde_json::Value>,
) -> Result<serde_json::Value, String> {
    // The first command from the webview means Svelte mounted and the window is
    // no longer blank, so this is the user-visible end of the launch.
    if !FIRST_UI_COMMAND_RECORDED.swap(true, Ordering::Relaxed) {
        record_startup_phase("ui_first_command");
        // The real interface is on screen now, so the launch splash has nothing
        // left to cover.
        #[cfg(windows)]
        splash::hide();
    }
    match command.as_str() {
        "snapshot"
        | "start"
        | "stop"
        | "start_lockpicking_observe"
        | "start_pickpocket_observe"
        | "toggle_pickpocket_observe"
        | "stop_pickpocket_observe"
        | "start_lockpicking_class_c"
        | "toggle_lockpicking_class_c"
        | "stop_lockpicking_observe"
        | "verify_setup"
        | "list_targets" => bridge.command(&app, &command, None, None),
        "select_target" if target_process_id.is_some() => {
            bridge.command(&app, &command, target_process_id, None)
        }
        "select_target" => Err("Target process ID is required.".into()),
        "save_settings" if settings.is_some() => bridge.command(&app, &command, None, settings),
        "configure_pickpocket" | "pickpocket_history" if settings.is_some() => {
            bridge.command(&app, &command, None, settings)
        }
        "configure_pickpocket" => Err("Settings payload is required.".into()),
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

    let recent_samples = read_tail_lines(
        &directory.join("last-fishing.csv"),
        MAX_RECENT_DIAGNOSTIC_TEXT_BYTES,
        60,
    );

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
    let recent_events = read_tail_lines(
        &directory.join("events.jsonl"),
        MAX_RECENT_DIAGNOSTIC_TEXT_BYTES,
        120,
    )
    .iter()
    .filter_map(|line| serde_json::from_str::<serde_json::Value>(line).ok())
    .collect::<Vec<_>>();
    let mut included_image_bytes = 0usize;
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
            let image_path = directory.join(image_name);
            let image_bytes = fs::metadata(&image_path).ok()?.len();
            let metadata = fs::read_to_string(directory.join(metadata_name))
                .ok()
                .and_then(|json| serde_json::from_str::<serde_json::Value>(&json).ok());
            let available_bytes = MAX_DEBUG_IMAGE_BYTES.saturating_sub(included_image_bytes);
            let embed_limit = MAX_SINGLE_DEBUG_IMAGE_BYTES.min(available_bytes);
            let can_embed = image_bytes <= embed_limit as u64;
            let image_data = if can_embed {
                fs::File::open(&image_path)
                    .ok()
                    .and_then(|file| read_limited(file, embed_limit))
                    .map(|bytes| {
                        included_image_bytes += bytes.len();
                        format!("data:image/png;base64,{}", BASE64.encode(bytes))
                    })
            } else {
                None
            };
            Some(serde_json::json!({
                "label": frame.get("label"),
                "score": frame.get("score"),
                "elapsedMilliseconds": frame.get("elapsedMilliseconds"),
                "imageName": image_name,
                "imageData": image_data,
                "imageAvailable": image_data.is_some(),
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

fn focus_main_window(app: &AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
    }
}

pub fn run() {
    let _ = LAUNCHED_AT.set(Instant::now());
    // Velopack must run before Tauri initializes. During install/update/remove
    // lifecycle hooks this may fast-exit without constructing the desktop UI.
    velopack::VelopackApp::build().run();
    record_startup_phase("velopack_done");
    // Everything past this point is a real launch, and the next five seconds are
    // spent inside Tauri/WebView2 with nothing able to paint. Put a native
    // window up now; it needs no web content, so it does not wait on any of it.
    #[cfg(windows)]
    splash::show();
    let previous_panic_hook = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        support::log("shell_panic", &info.to_string());
        previous_panic_hook(info);
    }));

    // Release-mode smoke packages can exercise the real installed Velopack
    // manager without constructing a Tauri window or touching production data.
    #[cfg(feature = "update-test-feed")]
    if update_service::run_smoke_from_args() {
        return;
    }

    let builder = tauri::Builder::default()
        // This must stay ahead of every other plugin so a second launch exits
        // before it can claim F10 or start a competing engine sidecar.
        .plugin(tauri_plugin_single_instance::init(
            |app, _arguments, _working_directory| focus_main_window(app),
        ))
        // Splits the launch gap further. Plugin initialization runs before the
        // configured window is created, so this timestamp says which side of
        // that line the missing seconds are on: near `builder_ready` means the
        // cost is window/WebView creation, near `setup_begin` means it is a
        // plugin.
        .plugin(
            tauri::plugin::Builder::new("startup-probe")
                .setup(|_app, _api: tauri::plugin::PluginApi<tauri::Wry, ()>| {
                    record_startup_phase("plugins_ready");
                    Ok(())
                })
                .build(),
        )
        .manage(EngineBridge::default())
        .manage(notifications::NotificationState::default())
        .manage(Arc::new(UpdateService::new()))
        .setup(|app| {
            use tauri_plugin_global_shortcut::ShortcutState;

            record_startup_phase("setup_begin");
            notifications::setup(app)?;
            record_startup_phase("notifications_ready");

            app.handle().plugin(
                tauri_plugin_global_shortcut::Builder::new()
                    .with_handler(move |handle, shortcut, event| {
                        let bridge = handle.state::<EngineBridge>().inner().clone();
                        if event.state() == ShortcutState::Pressed {
                            let Some(command) = bridge.command_for_shortcut(shortcut) else {
                                return;
                            };
                            run_shortcut_command(handle.clone(), bridge, command);
                        }
                    })
                    .build(),
            )?;
            let owns_global_shortcuts = shortcut_profile_owns_hotkeys(&app.config().identifier);
            let bridge = app.state::<EngineBridge>().inner().clone();
            bridge.set_shortcuts_enabled(owns_global_shortcuts);
            if owns_global_shortcuts {
                bridge.register_default_shortcuts(app.handle());
                #[cfg(windows)]
                mouse_shortcuts::install(app.handle().clone());
            }
            record_startup_phase("setup_end");
            // Normally the webview reports in about a tenth of a second later
            // and closes the splash itself. This bounds the damage when the
            // frontend fails to load: `hide` is idempotent, so the usual path
            // makes this a no-op.
            #[cfg(windows)]
            std::thread::spawn(|| {
                std::thread::sleep(std::time::Duration::from_secs(3));
                splash::hide();
            });
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            engine_command,
            shortcut_capture,
            support::support_sessions,
            support::support_report,
            support::support_health,
            support::open_evidence_session,
            support::export_evidence_report,
            support::support_log,
            diagnostics_snapshot,
            open_diagnostics,
            notifications::notification_ready,
            notifications::notification_settings,
            notifications::save_notification_settings,
            notifications::preview_notification,
            update_service::updater_status,
            update_service::check_for_updates,
            update_service::download_update,
            update_service::apply_pending_update,
            update_service::open_update_releases
        ])
        .on_window_event(|window, event| {
            if window.label() == "main" && matches!(event, tauri::WindowEvent::Destroyed) {
                let bridge = window.state::<EngineBridge>();
                bridge.shutdown(window.app_handle());
                // The hidden notification window must not keep CuePilot alive.
                window.app_handle().exit(0);
            }
        });
    // Splits the launch into our own pre-run work and the time Tauri/WebView2
    // spends bringing up the window. Without this marker the whole gap up to
    // `setup_begin` reads as one opaque delay.
    record_startup_phase("builder_ready");
    builder
        .run(tauri::generate_context!())
        .expect("error while running CuePilot Tauri shell");
}

/// Announces a fired shortcut and runs its engine command off the caller's thread.
/// Shared by the keyboard plugin handler and the mouse hook.
fn run_shortcut_command(handle: AppHandle, bridge: EngineBridge, command: &'static str) {
    use tauri::Emitter;

    let shortcut_label = bridge
        .registered_shortcut(command)
        .map(|text| engine_bridge::shortcut_label(&text))
        .unwrap_or_else(|| "the shortcut".to_string());
    let shortcut_payload = serde_json::json!({
        "name": "shortcut",
        "payload": {
            "key": shortcut_label,
            "command": command,
        },
    });
    publish_overlay(&handle, shortcut_payload.clone());
    let _ = handle.emit("engine://event", shortcut_payload);
    thread::spawn(move || match bridge.command(&handle, command, None, None) {
        Ok(result) => notifications::confirm_shortcut(&handle, command, &result, &shortcut_label),
        Err(detail) => {
            let fault_payload = serde_json::json!({
                "name": "fault",
                "payload": { "detail": detail },
            });
            publish_overlay(&handle, fault_payload.clone());
            let _ = handle.emit("engine://event", fault_payload);
        }
    });
}

/// Called from the low-level mouse hook. Returns true when the press was bound
/// to a command, so the hook swallows it instead of passing it to the game.
#[cfg(windows)]
pub(crate) fn dispatch_mouse_shortcut(app: &AppHandle, shortcut: &str) -> bool {
    let bridge = app.state::<EngineBridge>().inner().clone();
    let Some(command) = bridge.command_for_mouse(shortcut) else {
        return false;
    };
    run_shortcut_command(app.clone(), bridge, command);
    true
}

/// The Settings drawer calls this while its key-capture field is listening, so
/// the currently registered keys reach the page instead of firing commands.
#[tauri::command]
fn shortcut_capture(app: AppHandle, bridge: State<EngineBridge>, active: bool) {
    bridge.set_capture_suspended(&app, active);
}

fn shortcut_profile_owns_hotkeys(identifier: &str) -> bool {
    // Dev must exercise the same F10/F9/Pause path as the packaged product.
    // Registration itself is the safe arbiter: Windows refuses an already-held
    // shortcut and EngineBridge publishes the conflict as an in-app fault
    // instead of silently making the development build inert.
    let _ = identifier;
    true
}

#[cfg(test)]
mod diagnostics_tests {
    use super::*;
    use std::{
        cell::Cell,
        io,
        rc::Rc,
        time::{SystemTime, UNIX_EPOCH},
    };

    struct CountingReader {
        remaining: usize,
        bytes_read: Rc<Cell<usize>>,
    }

    impl io::Read for CountingReader {
        fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
            let count = self.remaining.min(buffer.len());
            buffer[..count].fill(b'x');
            self.remaining -= count;
            self.bytes_read.set(self.bytes_read.get() + count);
            Ok(count)
        }
    }

    #[test]
    fn all_profiles_attempt_to_register_global_shortcuts() {
        assert!(shortcut_profile_owns_hotkeys("com.blazzer.cuepilot"));
        assert!(shortcut_profile_owns_hotkeys("com.blazzer.cuepilot.dev"));
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

    #[test]
    fn tail_reader_bounds_input_and_keeps_complete_recent_lines() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("clock should be valid")
            .as_nanos();
        let path = std::env::temp_dir().join(format!("cuepilot-tail-test-{unique}.jsonl"));
        let content = (0..200)
            .map(|index| format!("{{\"sequence\":{index},\"detail\":\"{}\"}}", "x".repeat(64)))
            .collect::<Vec<_>>()
            .join("\n");
        fs::write(&path, format!("{content}\n")).expect("trace should be written");

        let lines = read_tail_lines(&path, 512, 5);

        assert_eq!(lines.len(), 5);
        assert!(lines
            .iter()
            .all(|line| serde_json::from_str::<serde_json::Value>(line).is_ok()));
        assert!(lines[0].contains("\"sequence\":195"));
        assert!(lines[4].contains("\"sequence\":199"));
        fs::remove_file(path).expect("temporary trace should be removed");
    }

    #[test]
    fn limited_reader_never_consumes_more_than_overflow_sentinel() {
        let bytes_read = Rc::new(Cell::new(0));
        let reader = CountingReader {
            remaining: 4096,
            bytes_read: Rc::clone(&bytes_read),
        };

        assert!(read_limited(reader, 128).is_none());
        assert_eq!(bytes_read.get(), 129);
    }

    #[test]
    fn latest_debug_session_skips_oversized_image_without_reading_it() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("clock should be valid")
            .as_nanos();
        let root = std::env::temp_dir().join(format!("cuepilot-large-image-test-{unique}"));
        let session = root.join("sessions").join("20260813-220000-test");
        fs::create_dir_all(&session).expect("session directory should be created");
        fs::write(
            session.join("session.json"),
            r#"{"frames":[{"imageName":"large.png","metadataName":"frame.json"}]}"#,
        )
        .expect("manifest should be written");
        fs::File::create(session.join("large.png"))
            .and_then(|file| file.set_len(MAX_SINGLE_DEBUG_IMAGE_BYTES as u64 + 1))
            .expect("large placeholder should be created");

        let snapshot = latest_debug_session(&root).expect("debug session should load");

        assert_eq!(snapshot["frames"][0]["imageAvailable"], false);
        assert!(snapshot["frames"][0]["imageData"].is_null());
        fs::remove_dir_all(root).expect("temporary diagnostics should be removed");
    }
}
