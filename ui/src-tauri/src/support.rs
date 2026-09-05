use serde_json::{json, Value};
use std::{
    fs,
    io::{Read, Seek, SeekFrom, Write},
    path::{Path, PathBuf},
    sync::Mutex,
    time::{SystemTime, UNIX_EPOCH},
};

static LOG_LOCK: Mutex<()> = Mutex::new(());

fn root() -> Result<PathBuf, String> {
    super::diagnostics_directory()
}

pub(crate) fn log(kind: &str, detail: &str) {
    let Ok(_guard) = LOG_LOCK.lock() else { return };
    let Ok(root) = root() else { return };
    let _ = fs::create_dir_all(&root);
    let path = root.join("shell.jsonl");
    if fs::metadata(&path).is_ok_and(|m| m.len() >= 1024 * 1024) {
        return;
    }
    let event = json!({"timeUnixMs": SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_millis(),
        "kind": kind, "detail": detail.chars().take(2048).collect::<String>(), "shellVersion": env!("CARGO_PKG_VERSION")});
    if let Ok(mut file) = fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(file, "{event}");
    }
}

#[tauri::command]
pub(crate) fn support_log(kind: String, detail: String) -> Result<(), String> {
    if !matches!(kind.as_str(), "frontend_error" | "frontend_rejection") {
        return Err("Unsupported support log kind.".into());
    }
    log(&kind, &detail);
    Ok(())
}

fn activity_directory(activity: &str) -> Result<PathBuf, String> {
    Ok(root()?.join(match activity {
        "fishing" => "sessions",
        "lockpicking" => "lockpicking",
        "pickpocket" => "pickpocket",
        _ => return Err("Unknown activity.".into()),
    }))
}

fn session_path(activity: &str, id: &str) -> Result<PathBuf, String> {
    if id.is_empty()
        || id.len() > 100
        || !id
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_')
    {
        return Err("Invalid session ID.".into());
    }
    let base = activity_directory(activity)?
        .canonicalize()
        .map_err(|_| "Evidence no longer available.")?;
    let path = base
        .join(id)
        .canonicalize()
        .map_err(|_| "Evidence no longer available.")?;
    if !path.starts_with(&base) || !path.is_dir() {
        return Err("Invalid evidence directory.".into());
    }
    Ok(path)
}

fn read_bounded(directory: &Path, name: &str, limit: u64) -> Option<String> {
    let path = directory.join(name).canonicalize().ok()?;
    let base = directory.canonicalize().ok()?;
    if !path.starts_with(base) {
        return None;
    }
    let mut text = String::new();
    fs::File::open(path)
        .ok()?
        .take(limit)
        .read_to_string(&mut text)
        .ok()?;
    Some(text)
}

fn read_tail(directory: &Path, name: &str, limit: u64) -> Option<String> {
    let path = directory.join(name).canonicalize().ok()?;
    if !path.starts_with(directory.canonicalize().ok()?) {
        return None;
    }
    let mut file = fs::File::open(path).ok()?;
    let length = file.metadata().ok()?.len();
    file.seek(SeekFrom::Start(length.saturating_sub(limit)))
        .ok()?;
    let mut text = String::new();
    file.read_to_string(&mut text).ok()?;
    if length > limit {
        text = text
            .split_once('\n')
            .map(|(_, rest)| rest.to_owned())
            .unwrap_or_default();
    }
    Some(text)
}

#[tauri::command]
pub(crate) fn support_sessions(activity: String, page: usize) -> Result<Value, String> {
    if page > 10000 {
        return Err("Invalid page.".into());
    }
    let directory = activity_directory(&activity)?;
    let mut sessions: Vec<_> = match fs::read_dir(directory) {
        Ok(entries) => entries
            .filter_map(Result::ok)
            .filter(|e| e.file_type().is_ok_and(|t| t.is_dir()))
            .collect(),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => vec![],
        Err(e) => return Err(e.to_string()),
    };
    sessions.sort_by_key(|e| std::cmp::Reverse(e.file_name()));
    let total = sessions.len();
    let mut total_bytes = 0u64;
    let mut storage_limited = sessions.len() > 200;
    for entry in sessions.iter().take(200) {
        let mut count = 0;
        if let Ok(files) = fs::read_dir(entry.path()) {
            for file in files.filter_map(Result::ok).take(4000) {
                count += 1;
                total_bytes =
                    total_bytes.saturating_add(file.metadata().map(|m| m.len()).unwrap_or(0));
            }
        }
        storage_limited |= count == 4000;
    }
    let page = page.min(total.saturating_sub(1) / 5);
    let entries: Vec<_> = sessions.iter().skip(page * 5).take(5).map(|entry| {
        let manifest = read_bounded(&entry.path(), "session.json", 524288).and_then(|s| serde_json::from_str::<Value>(&s).ok());
        let complete = entry.path().join("summary.json").exists() || manifest.as_ref().and_then(|v| v.get("active")).and_then(Value::as_bool) == Some(false);
        let files: Vec<_> = fs::read_dir(entry.path()).into_iter().flatten().filter_map(Result::ok).take(4000).collect();
        let bytes: u64 = files.iter().filter_map(|e| e.metadata().ok()).filter(|m| m.is_file()).map(|m| m.len()).sum();
        json!({"id": entry.file_name().to_string_lossy(), "complete":complete, "bytes":bytes, "sizeLimited": files.len() == 4000,
            "engineVersion": manifest.as_ref().and_then(|v| v.get("engineVersion"))})
    }).collect();
    Ok(
        json!({"sessions":entries,"total":total,"page":page,"pageSize":5,"totalBytes":total_bytes,"storageLimited":storage_limited}),
    )
}

#[tauri::command]
pub(crate) fn support_report(activity: String, session_id: String) -> Result<Value, String> {
    let directory = session_path(&activity, &session_id)?;
    let report = read_bounded(&directory, "REPORT.md", 65536);
    let trace = read_tail(
        &directory,
        if activity == "fishing" {
            "events.jsonl"
        } else {
            "trace.jsonl"
        },
        262144,
    )
    .unwrap_or_default();
    let mut decisions = Vec::new();
    for line in trace.lines() {
        if let Ok(value) = serde_json::from_str::<Value>(line) {
            let status = value.get("status").unwrap_or(&value);
            let detail = status
                .get("detail")
                .or_else(|| status.pointer("/prediction/reason"))
                .or_else(|| status.get("eventName"))
                .or_else(|| status.get("reason"));
            if let Some(detail) = detail.and_then(Value::as_str) {
                if decisions.last().is_none_or(|last| last != detail) {
                    decisions.push(detail.chars().take(500).collect::<String>());
                }
            }
        }
        if decisions.len() >= 30 {
            break;
        }
    }
    Ok(
        json!({"sessionId":session_id,"report":report.unwrap_or_else(|| "No finalized report is available. Inspect local evidence for incomplete recording details.".into()),"decisions":decisions,"bounded":true}),
    )
}

#[tauri::command]
pub(crate) fn open_evidence_session(activity: String, session_id: String) -> Result<(), String> {
    let path = session_path(&activity, &session_id)?;
    std::process::Command::new("explorer.exe")
        .arg(path)
        .spawn()
        .map_err(|e| e.to_string())?;
    Ok(())
}

#[tauri::command]
pub(crate) fn support_health() -> Result<Value, String> {
    let directory = root()?;
    let executable = std::env::current_exe().map_err(|e| e.to_string())?;
    let log_bytes = fs::metadata(directory.join("shell.jsonl"))
        .map(|m| m.len())
        .unwrap_or(0);
    Ok(
        json!({"shellVersion":env!("CARGO_PKG_VERSION"),"development":cfg!(debug_assertions),"processId":std::process::id(),
        "executableName":executable.file_name().map(|s| s.to_string_lossy().to_string()),"diagnosticsAvailable":directory.is_dir(),
        "logBytes":log_bytes,"logLimited":log_bytes >= 1024*1024,"protocolVersion":1,
        "detail":"Passive health snapshot. Capture and game input were not started."}),
    )
}

#[cfg(test)]
mod tests {
    #[test]
    fn rejects_paths_and_unknown_activity() {
        assert!(super::session_path("pickpocket", "../settings").is_err());
        assert!(super::session_path("pickpocket", "C:\\windows").is_err());
        assert!(super::activity_directory("../").is_err());
    }
}

#[tauri::command]
pub(crate) fn export_evidence_report(
    activity: String,
    session_id: String,
) -> Result<String, String> {
    let report = support_report(activity.clone(), session_id.clone())?;
    let export_root = root()?.join("exports");
    fs::create_dir_all(&export_root).map_err(|e| e.to_string())?;
    let stamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos();
    let destination = export_root.join(format!("{activity}-{session_id}-{stamp}"));
    fs::create_dir(&destination).map_err(|e| e.to_string())?;
    fs::write(
        destination.join("report.json"),
        serde_json::to_vec_pretty(&report).map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.to_string())?;
    fs::write(destination.join("manifest.json"), json!({"schemaVersion":1,"activity":activity,"sessionId":session_id,"included":["report.json"],"omitted":["screenshots","raw traces"],"note":"Local evidence may contain machine details. Review before sharing."}).to_string()).map_err(|e| e.to_string())?;
    Ok(format!(
        "Text evidence exported to {}",
        destination.display()
    ))
}
