//! Velopack update service and the narrow Tauri command surface used by the UI.
//!
//! Installed builds check CuePilot's public GitHub releases. Development builds
//! report that updating is unavailable unless `CUEPILOT_UPDATE_FEED` points at
//! a local Velopack feed. Every blocking Velopack call runs off the async/UI
//! thread, and apply shuts down the owned .NET sidecar before Tauri exits.

use std::sync::{Arc, Mutex, MutexGuard};

#[cfg(feature = "update-test-feed")]
use std::{io::Write, process::Stdio, time::Duration};

use serde::Serialize;
use tauri::{AppHandle, Emitter, State};
use velopack::sources::GithubSource;
use velopack::{UpdateCheck, UpdateInfo, UpdateManager};

use crate::engine_bridge::EngineBridge;

const UPDATE_REPOSITORY_URL: &str = "https://github.com/Blazzer10200/CuePilot";

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdaterRuntimeDto {
    pub installed: bool,
    pub development: bool,
    pub current_version: String,
    pub detail: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateInfoDto {
    pub version: String,
    pub release_name: String,
    pub size_bytes: u64,
    pub notes_markdown: String,
}

struct Inner {
    manager: Option<UpdateManager>,
    pending: Option<UpdateInfo>,
    downloaded: bool,
    busy: bool,
    applying: bool,
    init_error: Option<String>,
}

pub struct UpdateService {
    inner: Mutex<Inner>,
}

impl UpdateService {
    pub fn new() -> Self {
        let (manager, init_error) = match resolve_manager() {
            Ok(manager) => (Some(manager), None),
            Err(error) => (None, Some(error)),
        };
        Self {
            inner: Mutex::new(Inner {
                manager,
                pending: None,
                downloaded: false,
                busy: false,
                applying: false,
                init_error,
            }),
        }
    }

    fn lock(&self) -> MutexGuard<'_, Inner> {
        self.inner
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    pub fn runtime(&self) -> UpdaterRuntimeDto {
        let inner = self.lock();
        let installed = inner.manager.is_some();
        let development = cfg!(debug_assertions);
        let detail = if installed {
            "Velopack update service is ready.".to_string()
        } else if development {
            "Development builds are updated by the local toolchain, not Velopack.".to_string()
        } else {
            format!(
                "This copy is not a valid Velopack installation: {}",
                inner
                    .init_error
                    .as_deref()
                    .unwrap_or("the install manifest is unavailable")
            )
        };
        UpdaterRuntimeDto {
            installed,
            development,
            current_version: env!("CARGO_PKG_VERSION").to_string(),
            detail,
        }
    }

    /// Check the public release feed and retain the exact update plan returned
    /// by Velopack for the later download/apply calls.
    pub fn check(&self) -> Result<Option<UpdateInfoDto>, String> {
        let manager = {
            let mut inner = self.lock();
            if inner.busy || inner.applying {
                return Err("another update operation is already in progress".to_string());
            }
            let manager = inner.manager.clone().ok_or_else(|| {
                inner
                    .init_error
                    .clone()
                    .unwrap_or_else(|| "Velopack is unavailable for this copy".to_string())
            })?;
            inner.busy = true;
            manager
        };

        let result = manager.check_for_updates();
        let mut inner = self.lock();
        inner.busy = false;
        match result {
            Ok(UpdateCheck::UpdateAvailable(info)) => {
                let dto = update_dto(&info);
                inner.pending = Some(*info);
                inner.downloaded = false;
                Ok(Some(dto))
            }
            Ok(UpdateCheck::NoUpdateAvailable | UpdateCheck::RemoteIsEmpty) => {
                inner.pending = None;
                inner.downloaded = false;
                Ok(None)
            }
            Err(error) => Err(format!("check for updates: {error}")),
        }
    }

    /// Download the update selected by `check`, streaming progress through the
    /// supplied channel. Only a successful download arms apply.
    pub fn download(&self, progress: std::sync::mpsc::Sender<i16>) -> Result<(), String> {
        let (manager, pending) = {
            let mut inner = self.lock();
            if inner.busy || inner.applying {
                return Err("another update operation is already in progress".to_string());
            }
            let manager = inner
                .manager
                .clone()
                .ok_or_else(|| "Velopack is unavailable for this copy".to_string())?;
            let pending = inner
                .pending
                .clone()
                .ok_or_else(|| "no update is pending; check for updates first".to_string())?;
            inner.busy = true;
            inner.downloaded = false;
            (manager, pending)
        };

        let result = manager
            .download_updates(&pending, Some(progress))
            .map_err(|error| format!("download update: {error}"));
        let mut inner = self.lock();
        inner.busy = false;
        inner.downloaded = result.is_ok();
        result
    }

    /// Schedule Update.exe to wait for this process, replace the installation,
    /// and relaunch. The command layer performs sidecar shutdown and app exit.
    pub fn schedule_apply(&self) -> Result<(), String> {
        self.schedule_apply_with_args(Vec::new())
    }

    fn schedule_apply_with_args(&self, restart_args: Vec<String>) -> Result<(), String> {
        let (manager, pending) = {
            let mut inner = self.lock();
            if inner.busy || inner.applying {
                return Err("another update operation is already in progress".to_string());
            }
            if !inner.downloaded {
                return Err("the update has not finished downloading".to_string());
            }
            let manager = inner
                .manager
                .clone()
                .ok_or_else(|| "Velopack is unavailable for this copy".to_string())?;
            let pending = inner
                .pending
                .clone()
                .ok_or_else(|| "no update is pending; check for updates first".to_string())?;
            inner.applying = true;
            (manager, pending)
        };

        if let Err(error) = manager.wait_exit_then_apply_updates(&pending, true, true, restart_args)
        {
            self.lock().applying = false;
            return Err(format!("schedule update apply: {error}"));
        }
        Ok(())
    }

    #[cfg(feature = "update-test-feed")]
    fn installed_identity(&self) -> Result<(String, String), String> {
        let inner = self.lock();
        let manager = inner.manager.as_ref().ok_or_else(|| {
            inner
                .init_error
                .clone()
                .unwrap_or_else(|| "Velopack is unavailable".into())
        })?;
        Ok((
            manager.get_app_id(),
            manager.get_current_version_as_string(),
        ))
    }
}

impl Default for UpdateService {
    fn default() -> Self {
        Self::new()
    }
}

fn update_dto(info: &UpdateInfo) -> UpdateInfoDto {
    let asset = &info.TargetFullRelease;
    UpdateInfoDto {
        version: asset.Version.clone(),
        release_name: asset.FileName.clone(),
        size_bytes: asset.Size,
        notes_markdown: asset.NotesMarkdown.clone(),
    }
}

fn resolve_manager() -> Result<UpdateManager, String> {
    #[cfg(any(debug_assertions, feature = "update-test-feed"))]
    if let Ok(feed) = std::env::var("CUEPILOT_UPDATE_FEED") {
        let path = std::path::PathBuf::from(feed)
            .canonicalize()
            .map_err(|error| format!("resolve local update feed: {error}"))?;
        if !path.is_dir() {
            return Err(format!(
                "local update feed is not a directory: {}",
                path.display()
            ));
        }
        let source = velopack::sources::FileSource::new(&path);
        return UpdateManager::new(source, None, None)
            .map_err(|error| format!("initialize local Velopack feed: {error}"));
    }

    let source = GithubSource::new(UPDATE_REPOSITORY_URL, None, false);
    UpdateManager::new(source, None, None).map_err(|error| format!("initialize Velopack: {error}"))
}

#[cfg(feature = "update-test-feed")]
pub fn run_smoke_from_args() -> bool {
    let arguments = std::env::args().collect::<Vec<_>>();
    let Some(index) = arguments
        .iter()
        .position(|argument| argument == "--velopack-smoke")
    else {
        return false;
    };
    let Some(result_path) = arguments.get(index + 1).map(std::path::PathBuf::from) else {
        return true;
    };
    let Some(expected_version) = arguments.get(index + 2).cloned() else {
        write_smoke_result(
            &result_path,
            serde_json::json!({ "success": false, "phase": "error", "error": "expected version argument is missing" }),
        );
        return true;
    };

    if let Err(error) = run_smoke(&result_path, &expected_version) {
        let mut result = read_smoke_result(&result_path).unwrap_or_else(|| serde_json::json!({}));
        if let Some(object) = result.as_object_mut() {
            object.insert("success".into(), false.into());
            object.insert("phase".into(), "error".into());
            object.insert("error".into(), error.into());
        }
        write_smoke_result(&result_path, result);
    }
    true
}

#[cfg(feature = "update-test-feed")]
fn run_smoke(result_path: &std::path::Path, expected_version: &str) -> Result<(), String> {
    let service = UpdateService::new();
    let (app_id, current_version) = service.installed_identity()?;
    let previous = read_smoke_result(result_path);
    let prior_sidecar_stopped = previous
        .as_ref()
        .and_then(|value| value.get("sidecarStopped"))
        .and_then(serde_json::Value::as_bool)
        .unwrap_or(false);
    write_smoke_result(
        result_path,
        serde_json::json!({
            "success": false,
            "phase": "checking",
            "appId": app_id,
            "currentVersion": current_version,
            "expectedVersion": expected_version,
            "sidecarStopped": prior_sidecar_stopped,
        }),
    );

    match service.check()? {
        Some(update) => {
            let (progress_sender, progress_receiver) = std::sync::mpsc::channel();
            service.download(progress_sender)?;
            let progress = progress_receiver.into_iter().max().unwrap_or_default();
            let mut sidecar = start_smoke_sidecar()?;
            let restart_args = std::env::args().skip(1).collect::<Vec<_>>();
            service.schedule_apply_with_args(restart_args)?;
            let sidecar_stopped = stop_smoke_sidecar(&mut sidecar);
            write_smoke_result(
                result_path,
                serde_json::json!({
                    "success": false,
                    "phase": "scheduled",
                    "appId": app_id,
                    "fromVersion": current_version,
                    "targetVersion": update.version,
                    "downloadProgress": progress,
                    "sidecarStopped": sidecar_stopped,
                }),
            );
            if !sidecar_stopped {
                return Err("the isolated engine sidecar did not stop before apply".into());
            }
        }
        None => {
            let marker = std::env::current_exe()
                .ok()
                .and_then(|path| path.parent().map(|parent| parent.join("smoke-version.txt")))
                .and_then(|path| std::fs::read_to_string(path).ok())
                .map(|value| value.trim().to_string())
                .unwrap_or_default();
            let success = current_version == expected_version
                && marker == expected_version
                && prior_sidecar_stopped;
            write_smoke_result(
                result_path,
                serde_json::json!({
                    "success": success,
                    "phase": "complete",
                    "appId": app_id,
                    "currentVersion": current_version,
                    "expectedVersion": expected_version,
                    "installedMarker": marker,
                    "sidecarStopped": prior_sidecar_stopped,
                }),
            );
            if !success {
                return Err("the installed version, payload marker, or sidecar shutdown check did not match".into());
            }
        }
    }
    Ok(())
}

#[cfg(feature = "update-test-feed")]
fn start_smoke_sidecar() -> Result<std::process::Child, String> {
    let executable = std::env::current_exe()
        .map_err(|error| format!("locate smoke executable: {error}"))?
        .parent()
        .ok_or_else(|| "smoke executable has no parent directory".to_string())?
        .join("resources")
        .join("engine")
        .join("CuePilot.exe");
    std::process::Command::new(&executable)
        .arg("--ui-bridge")
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|error| {
            format!(
                "start isolated engine sidecar {}: {error}",
                executable.display()
            )
        })
}

#[cfg(feature = "update-test-feed")]
fn stop_smoke_sidecar(child: &mut std::process::Child) -> bool {
    if let Some(mut input) = child.stdin.take() {
        let _ = writeln!(
            input,
            r#"{{"id":"velopack-smoke-shutdown","command":"shutdown","processId":null,"settings":null}}"#
        );
        let _ = input.flush();
    }
    for _ in 0..60 {
        match child.try_wait() {
            Ok(Some(_)) => return true,
            Ok(None) => std::thread::sleep(Duration::from_millis(50)),
            Err(_) => return false,
        }
    }
    let _ = child.kill();
    let _ = child.wait();
    false
}

#[cfg(feature = "update-test-feed")]
fn write_smoke_result(path: &std::path::Path, value: serde_json::Value) {
    if let Some(parent) = path.parent() {
        let _ = std::fs::create_dir_all(parent);
    }
    if let Ok(json) = serde_json::to_vec_pretty(&value) {
        let _ = std::fs::write(path, json);
    }
}

#[cfg(feature = "update-test-feed")]
fn read_smoke_result(path: &std::path::Path) -> Option<serde_json::Value> {
    std::fs::read_to_string(path)
        .ok()
        .and_then(|json| serde_json::from_str(&json).ok())
}

#[tauri::command]
pub fn updater_status(service: State<'_, Arc<UpdateService>>) -> UpdaterRuntimeDto {
    service.runtime()
}

#[tauri::command]
pub async fn check_for_updates(
    service: State<'_, Arc<UpdateService>>,
) -> Result<Option<UpdateInfoDto>, String> {
    let service = service.inner().clone();
    tauri::async_runtime::spawn_blocking(move || service.check())
        .await
        .map_err(|error| format!("update check task: {error}"))?
}

#[tauri::command]
pub async fn download_update(
    app: AppHandle,
    service: State<'_, Arc<UpdateService>>,
) -> Result<(), String> {
    let service = service.inner().clone();
    let (sender, receiver) = std::sync::mpsc::channel::<i16>();
    let progress_app = app.clone();
    let progress = std::thread::spawn(move || {
        for percent in receiver {
            let _ = progress_app.emit("update-progress", percent.clamp(0, 100));
        }
    });

    let result = tauri::async_runtime::spawn_blocking(move || service.download(sender))
        .await
        .map_err(|error| format!("update download task: {error}"))?;
    let _ = progress.join();
    result
}

#[tauri::command]
pub async fn apply_pending_update(
    app: AppHandle,
    bridge: State<'_, EngineBridge>,
    service: State<'_, Arc<UpdateService>>,
) -> Result<(), String> {
    let service = service.inner().clone();
    tauri::async_runtime::spawn_blocking(move || service.schedule_apply())
        .await
        .map_err(|error| format!("update apply task: {error}"))??;

    // The packaged .NET engine lives inside the directory Velopack swaps. Stop
    // the owned child before exiting so it cannot hold the installation open.
    bridge.shutdown(&app);
    app.exit(0);
    Ok(())
}

#[tauri::command]
pub fn open_update_releases() -> Result<(), String> {
    std::process::Command::new("explorer.exe")
        .arg(format!("{UPDATE_REPOSITORY_URL}/releases/latest"))
        .spawn()
        .map(|_| ())
        .map_err(|error| format!("open CuePilot releases: {error}"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn update_dto_exposes_only_the_ui_contract() {
        let mut info = UpdateInfo::default();
        info.TargetFullRelease.Version = "5.2.0".to_string();
        info.TargetFullRelease.FileName = "CuePilot-5.2.0-full.nupkg".to_string();
        info.TargetFullRelease.Size = 42_000_000;
        info.TargetFullRelease.NotesMarkdown = "Safer updates".to_string();

        assert_eq!(
            update_dto(&info),
            UpdateInfoDto {
                version: "5.2.0".to_string(),
                release_name: "CuePilot-5.2.0-full.nupkg".to_string(),
                size_bytes: 42_000_000,
                notes_markdown: "Safer updates".to_string(),
            }
        );
    }
}
