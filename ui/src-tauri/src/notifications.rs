//! Desktop notices adapted from Clipping Software's non-activating overlay.
//! Timers and sound live in Rust so a minimized WebView cannot delay readiness.
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{
    collections::VecDeque,
    fs,
    sync::Mutex,
    time::{Duration, SystemTime, UNIX_EPOCH},
};
use tauri::{
    AppHandle, Emitter, Manager, PhysicalPosition, PhysicalSize, WebviewUrl, WebviewWindowBuilder,
};

const DURATION_MS: u64 = 4500;
const SHORTCUT_DURATION_MS: u64 = 3200;

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub(crate) struct Preferences {
    popups: bool,
    sound: bool,
    /// Confirm an in-game shortcut press with a popup, so the player knows the
    /// engine actually reacted without alt-tabbing to CuePilot.
    shortcuts: bool,
}

impl Default for Preferences {
    fn default() -> Self {
        Self {
            popups: true,
            sound: true,
            shortcuts: true,
        }
    }
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct Notice {
    activity: &'static str,
    title: &'static str,
    detail: String,
    duration_ms: u64,
    /// Informational notices skip the chime; it is reserved for game events.
    #[serde(skip)]
    silent: bool,
}

impl Notice {
    fn pickpocket() -> Self {
        Self {
            activity: "Pickpocket",
            title: "Ready for another pickpocket",
            detail: "Cooldown complete. You can start a new attempt.".into(),
            duration_ms: DURATION_MS,
            silent: false,
        }
    }
    fn fishing() -> Self {
        Self {
            activity: "Fishing",
            title: "Ready for the next catch",
            detail: "New cast started. CuePilot is watching for the meter.".into(),
            duration_ms: DURATION_MS,
            silent: false,
        }
    }
    fn background() -> Self {
        Self {
            activity: "Background",
            title: "Still running in the tray",
            detail: "Shortcuts keep working. Right-click the tray icon to quit.".into(),
            duration_ms: DURATION_MS,
            silent: true,
        }
    }
    /// Feedback for the pickpocket start/stop shortcut, built from the snapshot
    /// the engine returned for that press. `shortcut` is the bound key's label.
    fn pickpocket_shortcut(snapshot: &Value, shortcut: &str) -> Self {
        let pickpocket = &snapshot["pickpocket"];
        let observing = pickpocket["observing"].as_bool().unwrap_or(false);
        let (title, detail) = if observing {
            let armed = match pickpocket["inputMode"].as_str().unwrap_or("Observe") {
                "PrecisionAttempt" => "Armed for one precision tap.",
                "SingleAttempt" => "Armed for one wide-target tap.",
                _ => "Watching the minigame. Space stays manual.",
            };
            (
                "Pickpocket started",
                format!("{armed} Press {shortcut} again to stop."),
            )
        } else {
            (
                "Pickpocket stopped",
                "Observation ended and input is released.".to_string(),
            )
        };
        Self {
            activity: "Pickpocket",
            title,
            detail,
            duration_ms: SHORTCUT_DURATION_MS,
            silent: false,
        }
    }
}

#[derive(Default)]
struct Tracker {
    cooldown: Option<u64>,
    completed_deadline: u64,
    fishing_state: String,
}

impl Tracker {
    fn consume(&mut self, message: &Value, now: u64) -> Option<Notice> {
        let payload = &message["payload"];
        match message["name"].as_str() {
            Some("pickpocket_status") => {
                let deadline = payload["cooldownUntilUnixMs"].as_u64().unwrap_or(0);
                // Preserve an observed timer through Stop and target loss. The
                // cooldown still applies even after automatic input disarms.
                // Ignore expired historical values on startup/reconnect.
                if deadline > now && deadline > self.completed_deadline.saturating_add(1000) {
                    self.cooldown = Some(deadline);
                }
            }
            Some("status") => {
                let next = payload["state"].as_str().unwrap_or("");
                let new_cast = next == "Armed" && self.fishing_state == "Casting";
                self.fishing_state = next.to_owned();
                if new_cast {
                    return Some(Notice::fishing());
                }
            }
            Some("bridge_state") if payload["connected"] == false => {
                *self = Self::default();
            }
            _ => {}
        }
        None
    }

    fn tick(&mut self, now: u64) -> Option<Notice> {
        if self.cooldown.is_some_and(|deadline| now >= deadline) {
            self.completed_deadline = self.cooldown.take().unwrap_or(0);
            return Some(Notice::pickpocket());
        }
        None
    }
}

#[derive(Default)]
struct Delivery {
    preferences: Preferences,
    tracker: Tracker,
    ready: bool,
    pending: VecDeque<Notice>,
    visible_until: u64,
    background_announced: bool,
}

impl Delivery {
    /// The tray hint is shown on the first hide of each launch only.
    fn announce_background(&mut self) {
        if !self.background_announced {
            self.background_announced = true;
            self.enqueue(Notice::background());
        }
    }

    fn enqueue(&mut self, notice: Notice) {
        // Keep a bounded queue of meaningful transitions, not raw telemetry.
        if self.pending.len() == 4 {
            self.pending.pop_front();
        }
        self.pending.push_back(notice);
    }
}

#[derive(Default)]
pub(crate) struct NotificationState(Mutex<Delivery>);

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}

pub(crate) fn consume(app: &AppHandle, message: &Value) {
    if let Ok(mut state) = app.state::<NotificationState>().0.lock() {
        if message["name"] == "bridge_state" && message["payload"]["connected"] == false {
            state.pending.clear();
        }
        if let Some(notice) = state.tracker.consume(message, now_ms()) {
            state.enqueue(notice);
        }
    };
}

/// Queues a confirmation for a shortcut the engine just handled. Only the
/// pickpocket toggle has one today; other commands are ignored on purpose.
pub(crate) fn confirm_shortcut(app: &AppHandle, command: &str, result: &Value, shortcut: &str) {
    if command != "toggle_pickpocket_observe" {
        return;
    }
    if let Ok(mut state) = app.state::<NotificationState>().0.lock() {
        if state.preferences.shortcuts {
            state.enqueue(Notice::pickpocket_shortcut(result, shortcut));
        }
    }
}

pub(crate) fn announce_background(app: &AppHandle) {
    if let Ok(mut state) = app.state::<NotificationState>().0.lock() {
        state.announce_background();
    }
}

pub(crate) fn consume_snapshot(app: &AppHandle, snapshot: &Value) {
    // Restored cooldowns must also work before observation is started again.
    if snapshot.get("pickpocket").is_some() {
        consume(
            app,
            &serde_json::json!({"name": "pickpocket_status", "payload": snapshot["pickpocket"]}),
        );
    }
}

fn settings_path(app: &AppHandle) -> Result<std::path::PathBuf, String> {
    app.path()
        .app_config_dir()
        .map(|dir| dir.join("notifications.json"))
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub(crate) fn notification_settings(app: AppHandle) -> Result<Preferences, String> {
    app.state::<NotificationState>()
        .0
        .lock()
        .map(|s| s.preferences.clone())
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub(crate) fn save_notification_settings(
    app: AppHandle,
    settings: Preferences,
) -> Result<(), String> {
    let path = settings_path(&app)?;
    fs::create_dir_all(path.parent().ok_or("Missing settings directory")?)
        .map_err(|e| e.to_string())?;
    let binding = app.state::<NotificationState>();
    let mut state = binding.0.lock().map_err(|e| e.to_string())?;
    fs::write(
        path,
        serde_json::to_vec_pretty(&settings).map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.to_string())?;
    state.preferences = settings;
    Ok(())
}

#[tauri::command]
pub(crate) fn notification_ready(
    app: AppHandle,
    window: tauri::WebviewWindow,
) -> Result<(), String> {
    if window.label() != "notification" {
        return Err("Only the notification window can register its listener".into());
    }
    app.state::<NotificationState>()
        .0
        .lock()
        .map_err(|e| e.to_string())?
        .ready = true;
    Ok(())
}

#[tauri::command]
pub(crate) fn preview_notification(app: AppHandle, activity: String) -> Result<(), String> {
    let notice = match activity.as_str() {
        "pickpocket" => Notice::pickpocket(),
        "fishing" => Notice::fishing(),
        "shortcut" => {
            let shortcut = app
                .state::<crate::engine_bridge::EngineBridge>()
                .registered_shortcut("toggle_pickpocket_observe")
                .map(|text| crate::engine_bridge::shortcut_label(&text))
                .unwrap_or_else(|| "F7".to_string());
            Notice::pickpocket_shortcut(
                &serde_json::json!({"pickpocket": {"observing": true, "inputMode": "PrecisionAttempt"}}),
                &shortcut,
            )
        }
        _ => return Err("Choose pickpocket, fishing, or shortcut".into()),
    };
    app.state::<NotificationState>()
        .0
        .lock()
        .map_err(|e| e.to_string())?
        .enqueue(notice);
    Ok(())
}

// Coordinates are physical pixels; scale the card and inset like Clipping Software.
fn bounds(x: i32, y: i32, width: u32, height: u32, scale: f64) -> (i32, i32, u32, u32) {
    // The window carries an 18 px transparent gutter on every side so the card's
    // drop shadow can fade out inside it; see --overlay-gutter in overlay.css.
    // These figures are the 300x76 card plus that gutter, so the screen margin
    // below is small on purpose -- the gutter supplies most of the visual gap.
    let margin = ((6.0 * scale).round() as u32)
        .min(width.saturating_sub(1) / 2)
        .min(height.saturating_sub(1) / 2);
    let w = ((336.0 * scale).round() as u32)
        .min(width.saturating_sub(2 * margin))
        .max(1);
    let h = ((116.0 * scale).round() as u32)
        .min(height.saturating_sub(2 * margin))
        .max(1);
    (x + (width - margin - w) as i32, y + margin as i32, w, h)
}

fn pump(app: &AppHandle) -> Result<(), String> {
    let now = now_ms();
    let (notice, preferences, hide) = {
        let binding = app.state::<NotificationState>();
        let mut state = binding.0.lock().map_err(|e| e.to_string())?;
        if let Some(notice) = state.tracker.tick(now) {
            state.enqueue(notice);
        }
        let hide =
            state.visible_until > 0 && (now >= state.visible_until || !state.preferences.popups);
        if hide {
            state.visible_until = 0;
        }
        let notice = if state.ready && state.visible_until == 0 {
            state.pending.pop_front()
        } else {
            None
        };
        if let Some(notice) = notice.as_ref().filter(|_| state.preferences.popups) {
            state.visible_until = now + notice.duration_ms;
        }
        (notice, state.preferences.clone(), hide)
    };
    if !hide && notice.is_none() {
        return Ok(());
    }
    let window = app
        .get_webview_window("notification")
        .ok_or("Notification window unavailable")?;
    if hide {
        window.hide().map_err(|e| e.to_string())?;
    }
    if let Some(notice) = notice {
        if preferences.sound && !notice.silent {
            play_sound();
        }
        if preferences.popups {
            if let Some(monitor) = app.primary_monitor().map_err(|e| e.to_string())? {
                let area = monitor.work_area();
                let (x, y, w, h) = bounds(
                    area.position.x,
                    area.position.y,
                    area.size.width,
                    area.size.height,
                    monitor.scale_factor(),
                );
                window
                    .set_position(PhysicalPosition::new(x, y))
                    .map_err(|e| e.to_string())?;
                window
                    .set_size(PhysicalSize::new(w, h))
                    .map_err(|e| e.to_string())?;
            }
            app.emit_to("notification", "notification-message", notice)
                .map_err(|e| e.to_string())?;
            window.show().map_err(|e| e.to_string())?;
            window.set_always_on_top(true).map_err(|e| e.to_string())?;
        }
    }
    Ok(())
}

pub(crate) fn setup(app: &mut tauri::App) -> tauri::Result<()> {
    match settings_path(app.handle()).and_then(|path| match fs::read(path) {
        Ok(bytes) => serde_json::from_slice::<Preferences>(&bytes).map_err(|e| e.to_string()),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(Preferences::default()),
        Err(e) => Err(e.to_string()),
    }) {
        Ok(preferences) => {
            app.state::<NotificationState>()
                .0
                .lock()
                .expect("notification setup lock")
                .preferences = preferences;
        }
        Err(error) => crate::support::log("notification_settings", &error),
    }
    let window =
        WebviewWindowBuilder::new(app, "notification", WebviewUrl::App("index.html".into()))
            .title("CuePilot Notification")
            .inner_size(336.0, 116.0)
            .decorations(false)
            .transparent(true)
            .always_on_top(true)
            .skip_taskbar(true)
            .shadow(false)
            .resizable(false)
            .focused(false)
            .focusable(false)
            .visible(false)
            .build()?;
    window.set_ignore_cursor_events(true)?;
    let handle = app.handle().clone();
    std::thread::spawn(move || loop {
        std::thread::sleep(Duration::from_millis(200));
        if handle.get_webview_window("main").is_none() {
            break;
        }
        let app = handle.clone();
        if handle
            .run_on_main_thread(move || {
                if let Err(error) = pump(&app) {
                    crate::support::log("notification_delivery", &error);
                }
            })
            .is_err()
        {
            break;
        }
    });
    Ok(())
}

/// A soft two-note chime, synthesized once and cached.
///
/// The alternative is a named system sound, which is what this used to be
/// (`SystemAsterisk`) and which is the hard Windows alert ding. Generating the
/// waveform keeps it gentle without shipping an audio asset, so nothing has to
/// change in packaging.
#[cfg(target_os = "windows")]
fn chime() -> &'static [u8] {
    static WAV: std::sync::OnceLock<Vec<u8>> = std::sync::OnceLock::new();
    WAV.get_or_init(|| {
        const RATE: u32 = 44_100;
        const MILLIS: u32 = 420;
        // A5 and the major third above it. The second voice enters late so the
        // pair reads as one chime rather than two separate beeps.
        const VOICES: [(f64, f64); 2] = [(880.0, 0.0), (1108.73, 0.09)];

        let count = (RATE as f64 * MILLIS as f64 / 1000.0) as usize;
        let mut data = Vec::with_capacity(count * 2);
        for n in 0..count {
            let t = n as f64 / RATE as f64;
            let mut value = 0.0;
            for (frequency, start) in VOICES {
                let age = t - start;
                if age < 0.0 {
                    continue;
                }
                // The short attack keeps the onset from clicking; the
                // exponential decay is what makes it read as soft.
                let attack = (age / 0.008).min(1.0);
                let decay = (-age / 0.115).exp();
                value += (std::f64::consts::TAU * frequency * age).sin() * attack * decay * 0.5;
            }
            let sample = (value * 0.22 * f64::from(i16::MAX)) as i16;
            data.extend_from_slice(&sample.to_le_bytes());
        }

        let mut wav = Vec::with_capacity(44 + data.len());
        wav.extend_from_slice(b"RIFF");
        wav.extend_from_slice(&(36 + data.len() as u32).to_le_bytes());
        wav.extend_from_slice(b"WAVEfmt ");
        wav.extend_from_slice(&16u32.to_le_bytes()); // PCM header length
        wav.extend_from_slice(&1u16.to_le_bytes()); // uncompressed
        wav.extend_from_slice(&1u16.to_le_bytes()); // mono
        wav.extend_from_slice(&RATE.to_le_bytes());
        wav.extend_from_slice(&(RATE * 2).to_le_bytes()); // bytes per second
        wav.extend_from_slice(&2u16.to_le_bytes()); // block align
        wav.extend_from_slice(&16u16.to_le_bytes()); // bits per sample
        wav.extend_from_slice(b"data");
        wav.extend_from_slice(&(data.len() as u32).to_le_bytes());
        wav.extend_from_slice(&data);
        wav
    })
}

#[cfg(target_os = "windows")]
fn play_sound() {
    #[link(name = "winmm")]
    extern "system" {
        fn PlaySoundW(sound: *const u16, module: isize, flags: u32) -> i32;
    }
    const SND_ASYNC: u32 = 0x0000_0001;
    const SND_NODEFAULT: u32 = 0x0000_0002;
    const SND_MEMORY: u32 = 0x0000_0004;
    // Route through the system notification channel so the volume mixer still
    // governs it.
    const SND_SYSTEM: u32 = 0x0020_0000;

    // Playback is asynchronous, so the buffer has to outlive this call; `chime`
    // hands back a `'static` slice for exactly that reason.
    let wav = chime();
    unsafe {
        PlaySoundW(
            wav.as_ptr().cast(),
            0,
            SND_ASYNC | SND_MEMORY | SND_NODEFAULT | SND_SYSTEM,
        );
    }
}

#[cfg(not(target_os = "windows"))]
fn play_sound() {}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn cooldown_survives_stop_and_notifies_once_without_more_engine_samples() {
        let mut tracker = Tracker::default();
        tracker.consume(
            &json!({"name":"pickpocket_status","payload":{"cooldownUntilUnixMs":4000}}),
            1000,
        );
        tracker.consume(&json!({"name":"pickpocket_status","payload":{"observing":false,"cooldownUntilUnixMs":0}}), 2000);
        assert!(tracker.tick(3999).is_none());
        assert_eq!(tracker.tick(4000).unwrap().activity, "Pickpocket");
        tracker.consume(
            &json!({"name":"pickpocket_status","payload":{"cooldownUntilUnixMs":4001}}),
            4000,
        );
        assert!(tracker.tick(4001).is_none());
        assert!(tracker.tick(5000).is_none());
    }

    #[test]
    fn old_cooldowns_do_not_alert_and_new_cooldowns_replace_pending_deadlines() {
        let mut tracker = Tracker::default();
        for (deadline, now) in [(500, 1000), (4000, 1000), (6000, 2000)] {
            tracker.consume(
                &json!({"name":"pickpocket_status","payload":{"cooldownUntilUnixMs":deadline}}),
                now,
            );
        }
        assert!(tracker.tick(4000).is_none());
        assert!(tracker.tick(6000).is_some());
        tracker.consume(
            &json!({"name":"pickpocket_status","payload":{"cooldownUntilUnixMs":8000}}),
            7000,
        );
        assert!(tracker.tick(8000).is_some());
    }

    #[test]
    fn pickpocket_shortcut_confirmation_reflects_the_engine_response() {
        let started = Notice::pickpocket_shortcut(
            &json!({"pickpocket":{"observing":true,"inputMode":"PrecisionAttempt"}}),
            "Mouse 4",
        );
        assert_eq!(started.title, "Pickpocket started");
        assert_eq!(
            started.detail,
            "Armed for one precision tap. Press Mouse 4 again to stop."
        );
        assert_eq!(started.duration_ms, SHORTCUT_DURATION_MS);

        let observe = Notice::pickpocket_shortcut(
            &json!({"pickpocket":{"observing":true,"inputMode":"Observe"}}),
            "F7",
        );
        assert!(observe.detail.starts_with("Watching the minigame."));

        let stopped = Notice::pickpocket_shortcut(&json!({"pickpocket":{"observing":false}}), "F7");
        assert_eq!(stopped.title, "Pickpocket stopped");
        assert_eq!(stopped.activity, "Pickpocket");
    }

    #[test]
    fn preferences_default_shortcut_confirmations_on_for_older_files() {
        let loaded: Preferences = serde_json::from_str(r#"{"popups":false,"sound":true}"#).unwrap();
        assert!(!loaded.popups);
        assert!(loaded.shortcuts);
    }

    #[test]
    fn tray_hint_is_silent_and_queued_once_per_launch() {
        let mut delivery = Delivery::default();
        delivery.announce_background();
        delivery.announce_background();
        assert_eq!(delivery.pending.len(), 1);
        let notice = &delivery.pending[0];
        assert_eq!(notice.activity, "Background");
        assert!(notice.silent);
        assert!(serde_json::to_value(notice)
            .unwrap()
            .get("silent")
            .is_none());
    }

    #[test]
    fn disconnect_cancels_pending_readiness() {
        let mut tracker = Tracker::default();
        tracker.consume(
            &json!({"name":"pickpocket_status","payload":{"cooldownUntilUnixMs":4000}}),
            1000,
        );
        tracker.consume(
            &json!({"name":"bridge_state","payload":{"connected":false}}),
            2000,
        );
        assert!(tracker.tick(5000).is_none());
    }

    #[test]
    fn fishing_alerts_once_per_cast_not_per_telemetry_sample_or_preflight() {
        let mut tracker = Tracker::default();
        let mut alerts = 0;
        for state in [
            "Casting",
            "Casting",
            "Armed",
            "Armed",
            "Regulating",
            "Collecting",
            "Casting",
            "Armed",
            "Armed",
            "Stopped",
        ] {
            if tracker
                .consume(&json!({"name":"status","payload":{"state":state}}), 0)
                .is_some()
            {
                alerts += 1;
            }
        }
        assert_eq!(alerts, 2);
    }

    #[test]
    fn overlay_fits_scaled_and_negative_origin_monitors() {
        for (x, y, width, height) in [
            (0, 0, 1920, 1040),
            (-2560, -360, 2560, 1400),
            (1920, 48, 800, 560),
        ] {
            for scale in [1.0, 1.25, 1.5, 2.0, 3.0] {
                let (left, top, w, h) = bounds(x, y, width, height, scale);
                assert!(left >= x && top >= y);
                assert!(left + w as i32 <= x + width as i32);
                assert!(top + h as i32 <= y + height as i32);
            }
        }
    }
}
