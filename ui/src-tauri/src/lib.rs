use std::{
    fs,
    io::{BufRead, BufReader, Write},
    process::{Child, ChildStdin, Command, Stdio},
    sync::Mutex,
    thread,
};

use base64::{engine::general_purpose::STANDARD as BASE64, Engine as _};
use tauri::{AppHandle, Emitter, Manager, State};

struct EngineBridge {
    child: Mutex<Option<Child>>,
    input: Mutex<Option<ChildStdin>>,
}

impl EngineBridge {
    fn resource_path(app: &AppHandle) -> Result<std::path::PathBuf, String> {
        app.path()
            .resource_dir()
            .map_err(|error| error.to_string())
            .map(|path| {
                path.join("resources")
                    .join("engine")
                    .join("Workflow Looper.exe")
            })
    }

    fn ensure_started(&self, app: &AppHandle) -> Result<(), String> {
        if self
            .input
            .lock()
            .map_err(|_| "Engine bridge lock failed.")?
            .is_some()
        {
            return Ok(());
        }

        let executable = Self::resource_path(app)?;
        if !executable.exists() {
            return Err(format!(
                "Engine sidecar is missing: {}",
                executable.display()
            ));
        }
        let mut child = Command::new(executable)
            .arg("--ui-bridge")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .map_err(|error| format!("Start local engine: {error}"))?;
        let stdout = child.stdout.take().ok_or("Engine did not expose stdout.")?;
        let handle = app.clone();
        thread::spawn(move || {
            for line in BufReader::new(stdout).lines().map_while(Result::ok) {
                let payload: serde_json::Value = serde_json::from_str(&line).unwrap_or_else(|_| serde_json::json!({ "name": "fault", "payload": { "detail": "Engine returned invalid JSON." } }));
                let _ = handle.emit("engine://event", payload);
            }
        });
        *self
            .input
            .lock()
            .map_err(|_| "Engine bridge lock failed.")? = child.stdin.take();
        *self
            .child
            .lock()
            .map_err(|_| "Engine bridge lock failed.")? = Some(child);
        Ok(())
    }

    fn command(
        &self,
        app: &AppHandle,
        command: &str,
        delay_milliseconds: u32,
        settings: Option<serde_json::Value>,
    ) -> Result<(), String> {
        self.ensure_started(app)?;
        let request = serde_json::json!({
            "id": format!("{}-{}", command, std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_err(|error| error.to_string())?.as_millis()),
            "command": command,
            "delayMilliseconds": delay_milliseconds,
            "settings": settings,
        });
        let mut input = self
            .input
            .lock()
            .map_err(|_| "Engine bridge lock failed.")?;
        let stream = input.as_mut().ok_or("Engine input is unavailable.")?;
        writeln!(stream, "{request}").map_err(|error| format!("Send engine command: {error}"))?;
        stream
            .flush()
            .map_err(|error| format!("Flush engine command: {error}"))
    }

    fn shutdown(&self, app: &AppHandle) {
        let _ = self.command(app, "shutdown", 0, None);
        if let Ok(mut child) = self.child.lock() {
            if let Some(mut process) = child.take() {
                let _ = process.wait();
            }
        }
    }
}

#[tauri::command]
fn engine_command(
    app: AppHandle,
    bridge: State<'_, EngineBridge>,
    command: String,
    delay_milliseconds: Option<u32>,
    settings: Option<serde_json::Value>,
) -> Result<(), String> {
    match command.as_str() {
        "snapshot" | "start" | "stop" | "capture_target" => {
            bridge.command(&app, &command, delay_milliseconds.unwrap_or(0), None)
        }
        "save_settings" if settings.is_some() => bridge.command(&app, &command, 0, settings),
        "save_settings" => Err("Settings payload is required.".into()),
        _ => Err("Unsupported local engine command.".into()),
    }
}

fn diagnostics_directory() -> Result<std::path::PathBuf, String> {
    std::env::var("LOCALAPPDATA")
        .map_err(|_| "LOCALAPPDATA is unavailable.".to_string())
        .map(|root| {
            std::path::PathBuf::from(root)
                .join("WorkflowLooper")
                .join("diagnostics")
        })
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

    let latest_loss = fs::read_dir(&directory)
        .map_err(|error| format!("Read diagnostics directory: {error}"))?
        .filter_map(Result::ok)
        .filter(|entry| {
            entry
                .file_type()
                .map(|kind| kind.is_file())
                .unwrap_or(false)
                && entry
                    .file_name()
                    .to_string_lossy()
                    .starts_with("meter-loss-")
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
            let image_data = fs::read(entry.path())
                .ok()
                .filter(|bytes| bytes.len() <= 6 * 1024 * 1024)
                .map(|bytes| format!("data:image/png;base64,{}", BASE64.encode(bytes)));
            serde_json::json!({ "name": name, "imageData": image_data })
        });

    Ok(serde_json::json!({
        "directory": directory.display().to_string(),
        "recentSamples": recent_samples,
        "latestLoss": latest_loss,
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
        .manage(EngineBridge {
            child: Mutex::new(None),
            input: Mutex::new(None),
        })
        .plugin(tauri_plugin_dialog::init())
        .setup(|app| {
            use tauri_plugin_global_shortcut::{Code, GlobalShortcutExt, Shortcut, ShortcutState};

            let pause_break = Shortcut::new(None, Code::Pause);
            app.handle().plugin(
                tauri_plugin_global_shortcut::Builder::new()
                    .with_handler(move |handle, shortcut, event| {
                        if shortcut == &pause_break && event.state() == ShortcutState::Pressed {
                            let bridge = handle.state::<EngineBridge>();
                            let _ = bridge.command(handle, "stop", 0, None);
                        }
                    })
                    .build(),
            )?;
            app.global_shortcut().register(pause_break)?;
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
                bridge.shutdown(&window.app_handle());
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running Workflow Looper Tauri shell");
}
