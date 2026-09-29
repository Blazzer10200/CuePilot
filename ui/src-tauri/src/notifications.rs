//! Desktop notices adapted from Clipping Software's non-activating overlay.
//! Timers and sound live in Rust so a minimized WebView cannot delay readiness.
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{
    collections::VecDeque,
    fs,
    sync::{mpsc, Mutex},
    time::{Duration, SystemTime, UNIX_EPOCH},
};
use tauri::{
    AppHandle, Emitter, Manager, PhysicalPosition, PhysicalSize, WebviewUrl, WebviewWindowBuilder,
};

const DURATION_MS: u64 = 4500;
const SHORTCUT_DURATION_MS: u64 = 3200;
const ALERT_DURATION_MS: u64 = 7000;
/// A notice waits this long before it shows, so events that land together (a
/// status change and the shortcut reply that caused it) become one card.
const SETTLE_MS: u64 = 120;
/// Covers the overlay's exit transition; the window hides once it has played.
const EXIT_MS: u64 = 200;
/// Only attempts this recent are announced, so history loaded on connect stays quiet.
const RESULT_WINDOW_MS: u64 = 15_000;
/// Longest sleep between delivery passes; also how often the thread checks
/// that the main window still exists.
const IDLE_WAKE_MS: u64 = 1000;

#[derive(Clone, Copy, Default, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub(crate) enum Corner {
    #[default]
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,
}

impl Corner {
    fn edge(self) -> &'static str {
        match self {
            Self::TopLeft | Self::BottomLeft => "left",
            Self::TopRight | Self::BottomRight => "right",
        }
    }
}

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub(crate) struct Preferences {
    popups: bool,
    sound: bool,
    /// Confirm an in-game shortcut press with a popup, so the player knows the
    /// engine actually reacted without alt-tabbing to CuePilot.
    shortcuts: bool,
    corner: Corner,
}

impl Default for Preferences {
    fn default() -> Self {
        Self {
            popups: true,
            sound: true,
            shortcuts: true,
            corner: Corner::TopRight,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
enum Tone {
    Info,
    Success,
    Warning,
    Critical,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct Notice {
    /// Also the replacement channel: a newer notice for the same activity
    /// takes over the card instead of queueing behind it.
    activity: &'static str,
    icon: &'static str,
    tone: Tone,
    title: &'static str,
    detail: String,
    duration_ms: u64,
    /// Informational notices skip the chime; it is reserved for game events.
    #[serde(skip)]
    silent: bool,
    /// One-shot alerts that nothing re-arms survive a Critical notice.
    #[serde(skip)]
    durable: bool,
}

impl Notice {
    fn new(
        activity: &'static str,
        icon: &'static str,
        tone: Tone,
        title: &'static str,
        detail: impl Into<String>,
    ) -> Self {
        Self {
            activity,
            icon,
            tone,
            title,
            detail: detail.into(),
            duration_ms: if tone == Tone::Critical {
                ALERT_DURATION_MS
            } else {
                DURATION_MS
            },
            silent: false,
            durable: false,
        }
    }

    fn brief(mut self) -> Self {
        self.duration_ms = SHORTCUT_DURATION_MS;
        self
    }

    fn quiet(mut self) -> Self {
        self.silent = true;
        self
    }

    fn durable(mut self) -> Self {
        self.durable = true;
        self
    }

    /// Critical notices replace whatever is on screen and drop older pending ones.
    fn interrupts(&self) -> bool {
        self.tone == Tone::Critical
    }

    fn pickpocket_ready() -> Self {
        Self::new(
            "Pickpocket",
            "hand",
            Tone::Info,
            "Ready for another pickpocket",
            "Cooldown complete. You can start a new attempt.",
        )
        .durable()
    }

    fn fishing_cast() -> Self {
        Self::new(
            "Fishing",
            "fish",
            Tone::Info,
            "Ready for the next catch",
            "New cast started. CuePilot is watching for the meter.",
        )
    }

    fn background() -> Self {
        Self::new(
            "Background",
            "tray",
            Tone::Info,
            "Still running in the tray",
            "Shortcuts keep working. Right-click the tray icon to quit.",
        )
        .quiet()
    }

    /// The newest saved attempt; `Ended` (no visible result) is not announced.
    fn pickpocket_result(attempt: &Value) -> Option<Self> {
        let item = attempt["itemName"].as_str().filter(|name| !name.is_empty());
        match attempt["outcome"].as_str()? {
            "Grabbed" => Some(Self::new(
                "Pickpocket",
                "check",
                Tone::Success,
                "Pickpocket grabbed",
                match item {
                    Some(item) => {
                        format!("Took the {item}. You'll get an alert when the cooldown ends.")
                    }
                    None => "You'll get an alert when the cooldown ends.".to_string(),
                },
            )),
            "Missed" => Some(Self::new(
                "Pickpocket",
                "miss",
                Tone::Warning,
                "Pickpocket missed",
                "You'll get an alert when the cooldown ends.",
            )),
            _ => None,
        }
    }

    /// Feedback for an in-game shortcut, built from the snapshot the engine
    /// returned for that press. `shortcut` is the bound key's label.
    fn shortcut(command: &str, snapshot: &Value, shortcut: &str) -> Option<Self> {
        match command {
            "toggle_pickpocket_observe" => {
                let pickpocket = &snapshot["pickpocket"];
                let notice = if pickpocket["observing"].as_bool().unwrap_or(false) {
                    let armed = match pickpocket["inputMode"].as_str().unwrap_or("Observe") {
                        "PrecisionAttempt" => "Armed for one precision tap.",
                        "SingleAttempt" => "Armed for one wide-target tap.",
                        _ => "Watching the minigame. Space stays manual.",
                    };
                    Self::new(
                        "Pickpocket",
                        "hand",
                        Tone::Info,
                        "Pickpocket started",
                        format!("{armed} Press {shortcut} again to stop."),
                    )
                } else {
                    Self::new(
                        "Pickpocket",
                        "stop",
                        Tone::Info,
                        "Pickpocket stopped",
                        "Observation ended and input is released.",
                    )
                };
                Some(notice.brief())
            }
            // Stopping is announced by the status change, which carries the reason.
            "toggle" => match snapshot["routineState"].as_str() {
                Some("Stopped" | "Faulted") | None => None,
                Some(_) => Some(
                    Self::new(
                        "Fishing",
                        "fish",
                        Tone::Info,
                        "Fishing started",
                        format!("Watching for the meter. Press {shortcut} again to stop."),
                    )
                    .brief(),
                ),
            },
            "stop" => Some(Self::new(
                "Safety",
                "stop",
                Tone::Critical,
                "Emergency stop",
                "Fishing and pickpocket are stopped and input is released.",
            )),
            _ => None,
        }
    }

    fn shortcut_failed(command: &str, detail: &str) -> Self {
        let activity = match command {
            "toggle" => "Fishing",
            "toggle_pickpocket_observe" => "Pickpocket",
            _ => "Safety",
        };
        // A failed emergency stop leaves input live, so it must break through.
        let tone = if command == "stop" {
            Tone::Critical
        } else {
            Tone::Warning
        };
        Self::new(activity, "alert", tone, "Shortcut didn't run", detail)
    }
}

#[derive(Default)]
struct Tracker {
    cooldown: Option<u64>,
    completed_deadline: u64,
    fishing_state: String,
    pickpocket_state: String,
    last_attempt: Option<String>,
    engine_lost: bool,
}

/// States in which a run is live, so leaving them is worth announcing.
fn fishing_running(state: &str) -> bool {
    !matches!(state, "" | "Stopped" | "Faulted")
}

fn pickpocket_running(state: &str) -> bool {
    matches!(state, "Searching" | "Tracking" | "Cooldown")
}

impl Tracker {
    fn consume(&mut self, message: &Value, now: u64) -> Option<Notice> {
        let payload = &message["payload"];
        let detail = payload["detail"].as_str().unwrap_or_default();
        match message["name"].as_str() {
            Some("pickpocket_status") => {
                let deadline = payload["cooldownUntilUnixMs"].as_u64().unwrap_or(0);
                // Preserve an observed timer through Stop and target loss. The
                // cooldown still applies even after automatic input disarms.
                // Ignore expired historical values on startup/reconnect.
                if deadline > now && deadline > self.completed_deadline.saturating_add(1000) {
                    self.cooldown = Some(deadline);
                }
                let result = self.new_attempt(&payload["recentAttempts"][0], now);
                let previous = std::mem::replace(
                    &mut self.pickpocket_state,
                    payload["state"].as_str().unwrap_or_default().to_owned(),
                );
                let next = self.pickpocket_state.as_str();
                result.or_else(|| {
                    if next == "Faulted" && !previous.is_empty() && previous != "Faulted" {
                        Some(Notice::new(
                            "Pickpocket",
                            "alert",
                            Tone::Critical,
                            "Pickpocket stopped on a problem",
                            detail,
                        ))
                    } else if next == "Waiting" && pickpocket_running(&previous) {
                        // Tabbing out is usually deliberate, so this one is silent.
                        Some(
                            Notice::new(
                                "Pickpocket",
                                "pause",
                                Tone::Warning,
                                "Pickpocket paused",
                                detail,
                            )
                            .quiet(),
                        )
                    } else {
                        None
                    }
                })
            }
            Some("status") => {
                let previous = std::mem::replace(
                    &mut self.fishing_state,
                    payload["state"].as_str().unwrap_or_default().to_owned(),
                );
                match self.fishing_state.as_str() {
                    "Armed" if previous == "Casting" => Some(Notice::fishing_cast()),
                    "Faulted" if fishing_running(&previous) => Some(Notice::new(
                        "Fishing",
                        "alert",
                        Tone::Critical,
                        "Fishing stopped on a problem",
                        detail,
                    )),
                    "Stopped" if fishing_running(&previous) => Some(Notice::new(
                        "Fishing",
                        "stop",
                        Tone::Info,
                        "Fishing stopped",
                        detail,
                    )),
                    _ => None,
                }
            }
            Some("bridge_state") if payload["connected"] == false => {
                // Nothing tracked before the loss is trusted; pending readiness is cancelled.
                *self = Self {
                    engine_lost: true,
                    ..Self::default()
                };
                Some(Notice::new(
                    "Engine",
                    "unplug",
                    Tone::Critical,
                    "Engine disconnected",
                    detail,
                ))
            }
            Some("bridge_state") if payload["connected"] == true && self.engine_lost => {
                self.engine_lost = false;
                Some(Notice::new(
                    "Engine",
                    "plug",
                    Tone::Success,
                    "Engine reconnected",
                    "Activities and shortcuts are available again.",
                ))
            }
            _ => None,
        }
    }

    /// Announces the newest saved attempt once, and only if it just ended.
    fn new_attempt(&mut self, attempt: &Value, now: u64) -> Option<Notice> {
        let id = attempt["id"].as_str()?;
        if self.last_attempt.as_deref() == Some(id) {
            return None;
        }
        self.last_attempt = Some(id.to_owned());
        let ended = attempt["endedAtUnixMs"].as_u64().unwrap_or(0);
        if ended.saturating_add(RESULT_WINDOW_MS) < now {
            return None;
        }
        Notice::pickpocket_result(attempt)
    }

    fn tick(&mut self, now: u64) -> Option<Notice> {
        if self.cooldown.is_some_and(|deadline| now >= deadline) {
            self.completed_deadline = self.cooldown.take().unwrap_or(0);
            return Some(Notice::pickpocket_ready());
        }
        None
    }
}

struct Queued {
    notice: Notice,
    at: u64,
}

#[derive(Debug)]
enum Action {
    Idle,
    Show(Notice),
    /// Play the card's exit transition; the window hides after `EXIT_MS`.
    Dismiss,
    Hide,
}

#[derive(Default)]
struct Delivery {
    preferences: Preferences,
    tracker: Tracker,
    ready: bool,
    pending: VecDeque<Queued>,
    /// Activity of the card on screen, if any.
    showing: Option<&'static str>,
    visible_until: u64,
    hide_at: u64,
    background_announced: bool,
    wake: Option<mpsc::Sender<()>>,
}

impl Delivery {
    /// The tray hint is shown on the first hide of each launch only.
    fn announce_background(&mut self, now: u64) {
        if !self.background_announced {
            self.background_announced = true;
            self.enqueue(Notice::background(), now);
        }
    }

    fn enqueue(&mut self, notice: Notice, now: u64) {
        // Keep a bounded queue of meaningful transitions, not raw telemetry.
        if notice.interrupts() {
            self.pending.retain(|queued| queued.notice.durable);
        } else {
            self.pending
                .retain(|queued| queued.notice.activity != notice.activity);
        }
        if self.pending.len() == 4 {
            self.pending.pop_front();
        }
        self.pending.push_back(Queued { notice, at: now });
        self.wake();
    }

    fn wake(&self) {
        if let Some(wake) = &self.wake {
            let _ = wake.send(());
        }
    }

    /// One delivery pass. A queued notice replaces the card at once when it
    /// belongs to the same activity or is critical; otherwise it waits for the
    /// card to time out and then swaps in without the window closing between.
    fn advance(&mut self, now: u64) -> Action {
        if let Some(notice) = self.tracker.tick(now) {
            self.enqueue(notice, now);
        }
        if self.showing.is_some() && !self.preferences.popups {
            self.showing = None;
            self.visible_until = 0;
            self.hide_at = 0;
            return Action::Hide;
        }
        let expired = now >= self.visible_until;
        let fits = self.pending.iter().position(|queued| {
            now >= queued.at + SETTLE_MS
                && (self.showing.is_none()
                    || expired
                    || self.showing == Some(queued.notice.activity)
                    || queued.notice.interrupts())
        });
        if let Some(index) = fits.filter(|_| self.ready) {
            if let Some(Queued { notice, .. }) = self.pending.remove(index) {
                if self.preferences.popups {
                    self.showing = Some(notice.activity);
                    self.visible_until = now + notice.duration_ms;
                    self.hide_at = 0;
                }
                return Action::Show(notice);
            }
        }
        if self.showing.is_some() && expired {
            self.showing = None;
            self.visible_until = 0;
            self.hide_at = now + EXIT_MS;
            return Action::Dismiss;
        }
        if self.hide_at > 0 && now >= self.hide_at {
            self.hide_at = 0;
            return Action::Hide;
        }
        Action::Idle
    }

    /// How long the delivery thread may sleep before the next deadline.
    fn next_wake(&self, now: u64) -> Duration {
        let mut deadlines = vec![now + IDLE_WAKE_MS];
        // A settled notice still pending is waiting for the card to expire,
        // which is already a deadline below.
        if self.ready {
            deadlines.extend(
                self.pending
                    .iter()
                    .map(|queued| queued.at + SETTLE_MS)
                    .filter(|&settled| settled > now),
            );
        }
        if self.showing.is_some() {
            deadlines.push(self.visible_until);
        }
        if self.hide_at > 0 {
            deadlines.push(self.hide_at);
        }
        deadlines.extend(self.tracker.cooldown);
        let next = deadlines.into_iter().min().unwrap_or(now);
        Duration::from_millis(next.saturating_sub(now).max(5))
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
        let now = now_ms();
        if let Some(notice) = state.tracker.consume(message, now) {
            state.enqueue(notice, now);
        }
    };
}

/// Queues a confirmation for a shortcut the engine just handled.
pub(crate) fn confirm_shortcut(app: &AppHandle, command: &str, result: &Value, shortcut: &str) {
    if let Ok(mut state) = app.state::<NotificationState>().0.lock() {
        if state.preferences.shortcuts {
            if let Some(notice) = Notice::shortcut(command, result, shortcut) {
                state.enqueue(notice, now_ms());
            }
        }
    }
}

/// A shortcut the engine refused. Shown even with confirmations off: from
/// inside the game this is the only sign the press did nothing.
pub(crate) fn shortcut_failed(app: &AppHandle, command: &str, detail: &str) {
    if let Ok(mut state) = app.state::<NotificationState>().0.lock() {
        state.enqueue(Notice::shortcut_failed(command, detail), now_ms());
    }
}

pub(crate) fn announce_background(app: &AppHandle) {
    if let Ok(mut state) = app.state::<NotificationState>().0.lock() {
        state.announce_background(now_ms());
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

/// A crash mid-write must not leave a truncated file, so write beside it and rename.
fn write_atomic(path: &std::path::Path, bytes: &[u8]) -> std::io::Result<()> {
    let temp = path.with_extension("json.tmp");
    fs::write(&temp, bytes)?;
    fs::rename(&temp, path).inspect_err(|_| {
        let _ = fs::remove_file(&temp);
    })
}

/// Unknown fields are ignored and a bad value resets only its own field, so a
/// file from another build does not wipe the rest.
fn parse_preferences(bytes: &[u8]) -> Result<Preferences, String> {
    let value: Value = serde_json::from_slice(bytes).map_err(|e| e.to_string())?;
    let object = value
        .as_object()
        .ok_or("notifications.json is not an object")?;
    let mut preferences = Preferences::default();
    for (name, slot) in [
        ("popups", &mut preferences.popups),
        ("sound", &mut preferences.sound),
        ("shortcuts", &mut preferences.shortcuts),
    ] {
        if let Some(flag) = object.get(name).and_then(Value::as_bool) {
            *slot = flag;
        }
    }
    if let Some(corner) = object
        .get("corner")
        .and_then(|corner| serde_json::from_value(corner.clone()).ok())
    {
        preferences.corner = corner;
    }
    Ok(preferences)
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
    write_atomic(
        &path,
        &serde_json::to_vec_pretty(&settings).map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.to_string())?;
    state.preferences = settings;
    state.wake();
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
    let binding = app.state::<NotificationState>();
    let mut state = binding.0.lock().map_err(|e| e.to_string())?;
    state.ready = true;
    state.wake();
    Ok(())
}

#[tauri::command]
pub(crate) fn preview_notification(app: AppHandle, activity: String) -> Result<(), String> {
    let notice = match activity.as_str() {
        "pickpocket" => Notice::pickpocket_ready(),
        "fishing" => Notice::fishing_cast(),
        "result" => Notice::pickpocket_result(
            &serde_json::json!({"outcome": "Grabbed", "itemName": "Wallet"}),
        )
        .ok_or("Preview result unavailable")?,
        "shortcut" => {
            let shortcut = app
                .state::<crate::engine_bridge::EngineBridge>()
                .registered_shortcut("toggle_pickpocket_observe")
                .map(|text| crate::engine_bridge::shortcut_label(&text))
                .unwrap_or_else(|| "F7".to_string());
            Notice::shortcut(
                "toggle_pickpocket_observe",
                &serde_json::json!({"pickpocket": {"observing": true, "inputMode": "PrecisionAttempt"}}),
                &shortcut,
            )
            .ok_or("Preview shortcut unavailable")?
        }
        _ => return Err("Choose pickpocket, fishing, result, or shortcut".into()),
    };
    app.state::<NotificationState>()
        .0
        .lock()
        .map_err(|e| e.to_string())?
        .enqueue(notice, now_ms());
    Ok(())
}

// Coordinates are physical pixels; scale the card and inset like Clipping Software.
fn bounds(
    x: i32,
    y: i32,
    width: u32,
    height: u32,
    scale: f64,
    corner: Corner,
) -> (i32, i32, u32, u32) {
    // The window carries an 18 px transparent gutter on every side so the card's
    // drop shadow can fade out inside it; see --overlay-gutter in overlay.css.
    // These figures are the 320x88 card plus that gutter, so the screen margin
    // below is small on purpose -- the gutter supplies most of the visual gap.
    let margin = ((6.0 * scale).round() as u32)
        .min(width.saturating_sub(1) / 2)
        .min(height.saturating_sub(1) / 2);
    let w = ((356.0 * scale).round() as u32)
        .min(width.saturating_sub(2 * margin))
        .max(1);
    let h = ((124.0 * scale).round() as u32)
        .min(height.saturating_sub(2 * margin))
        .max(1);
    let left = match corner {
        Corner::TopLeft | Corner::BottomLeft => x + margin as i32,
        Corner::TopRight | Corner::BottomRight => x + (width - margin - w) as i32,
    };
    let top = match corner {
        Corner::TopLeft | Corner::TopRight => y + margin as i32,
        Corner::BottomLeft | Corner::BottomRight => y + (height - margin - h) as i32,
    };
    (left, top, w, h)
}

/// The monitor under the window the player is using, so the card follows FiveM
/// to whichever screen it is on. The notice window itself never takes focus.
#[cfg(target_os = "windows")]
fn active_monitor(app: &AppHandle) -> Option<tauri::Monitor> {
    use windows_sys::Win32::{
        Foundation::RECT,
        UI::WindowsAndMessaging::{GetForegroundWindow, GetWindowRect},
    };
    let mut rect = RECT {
        left: 0,
        top: 0,
        right: 0,
        bottom: 0,
    };
    // SAFETY: both calls only read window state into a stack RECT.
    let found = unsafe {
        let window = GetForegroundWindow();
        !window.is_null() && GetWindowRect(window, &mut rect) != 0
    };
    if !found {
        return None;
    }
    let x = f64::from(rect.left) + f64::from(rect.right - rect.left) / 2.0;
    let y = f64::from(rect.top) + f64::from(rect.bottom - rect.top) / 2.0;
    app.monitor_from_point(x, y).ok().flatten()
}

#[cfg(not(target_os = "windows"))]
fn active_monitor(_app: &AppHandle) -> Option<tauri::Monitor> {
    None
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct Shown<'a> {
    #[serde(flatten)]
    notice: &'a Notice,
    edge: &'static str,
}

fn pump(app: &AppHandle) -> Result<Duration, String> {
    let now = now_ms();
    let (action, preferences, wait) = {
        let binding = app.state::<NotificationState>();
        let mut state = binding.0.lock().map_err(|e| e.to_string())?;
        let action = state.advance(now);
        (action, state.preferences.clone(), state.next_wake(now))
    };
    let window = || {
        app.get_webview_window("notification")
            .ok_or("Notification window unavailable")
    };
    match action {
        Action::Idle => {}
        Action::Hide => window()?.hide().map_err(|e| e.to_string())?,
        Action::Dismiss => app
            .emit_to("notification", "notification-dismiss", ())
            .map_err(|e| e.to_string())?,
        Action::Show(notice) => {
            if preferences.sound && !notice.silent {
                play_sound();
            }
            if preferences.popups {
                let window = window()?;
                // A replacement keeps the card where it is; only a fresh card is placed.
                if !window.is_visible().map_err(|e| e.to_string())? {
                    let monitor = match active_monitor(app) {
                        Some(monitor) => Some(monitor),
                        None => app.primary_monitor().map_err(|e| e.to_string())?,
                    };
                    if let Some(monitor) = monitor {
                        let area = monitor.work_area();
                        let (x, y, w, h) = bounds(
                            area.position.x,
                            area.position.y,
                            area.size.width,
                            area.size.height,
                            monitor.scale_factor(),
                            preferences.corner,
                        );
                        window
                            .set_position(PhysicalPosition::new(x, y))
                            .map_err(|e| e.to_string())?;
                        window
                            .set_size(PhysicalSize::new(w, h))
                            .map_err(|e| e.to_string())?;
                    }
                }
                let shown = Shown {
                    notice: &notice,
                    edge: preferences.corner.edge(),
                };
                app.emit_to("notification", "notification-message", shown)
                    .map_err(|e| e.to_string())?;
                window.show().map_err(|e| e.to_string())?;
                window.set_always_on_top(true).map_err(|e| e.to_string())?;
            }
        }
    }
    Ok(wait)
}

pub(crate) fn setup(app: &mut tauri::App) -> tauri::Result<()> {
    let (wake, woken) = mpsc::channel::<()>();
    {
        let binding = app.state::<NotificationState>();
        let mut state = binding.0.lock().expect("notification setup lock");
        state.wake = Some(wake);
        match settings_path(app.handle()).and_then(|path| match fs::read(path) {
            Ok(bytes) => parse_preferences(&bytes),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(Preferences::default()),
            Err(e) => Err(e.to_string()),
        }) {
            Ok(preferences) => state.preferences = preferences,
            Err(error) => crate::support::log("notification_settings", &error),
        }
    }
    let window =
        WebviewWindowBuilder::new(app, "notification", WebviewUrl::App("index.html".into()))
            .title("CuePilot Notification")
            .inner_size(356.0, 124.0)
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
    // Sleeps until the next deadline or until a notice is queued, so timing is
    // exact without polling while nothing is happening.
    std::thread::spawn(move || {
        let mut wait = Duration::ZERO;
        loop {
            let _ = woken.recv_timeout(wait);
            if handle.get_webview_window("main").is_none() {
                break;
            }
            let (reply, replied) = mpsc::channel();
            let app = handle.clone();
            if handle
                .run_on_main_thread(move || {
                    let wait = pump(&app).unwrap_or_else(|error| {
                        crate::support::log("notification_delivery", &error);
                        Duration::from_millis(250)
                    });
                    let _ = reply.send(wait);
                })
                .is_err()
            {
                break;
            }
            wait = replied
                .recv_timeout(Duration::from_millis(IDLE_WAKE_MS))
                .unwrap_or(Duration::from_millis(250));
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

    fn pickpocket(payload: Value) -> Value {
        json!({"name": "pickpocket_status", "payload": payload})
    }

    fn fishing(state: &str) -> Value {
        json!({"name": "status", "payload": {"state": state, "detail": format!("{state} detail")}})
    }

    fn ready_delivery() -> Delivery {
        Delivery {
            ready: true,
            ..Delivery::default()
        }
    }

    fn shown(action: Action) -> Notice {
        match action {
            Action::Show(notice) => notice,
            other => panic!("expected a notice, got {other:?}"),
        }
    }

    #[test]
    fn cooldown_survives_stop_and_notifies_once_without_more_engine_samples() {
        let mut tracker = Tracker::default();
        tracker.consume(&pickpocket(json!({"cooldownUntilUnixMs":4000})), 1000);
        tracker.consume(
            &pickpocket(json!({"observing":false,"cooldownUntilUnixMs":0})),
            2000,
        );
        assert!(tracker.tick(3999).is_none());
        assert_eq!(tracker.tick(4000).unwrap().activity, "Pickpocket");
        tracker.consume(&pickpocket(json!({"cooldownUntilUnixMs":4001})), 4000);
        assert!(tracker.tick(4001).is_none());
        assert!(tracker.tick(5000).is_none());
    }

    #[test]
    fn old_cooldowns_do_not_alert_and_new_cooldowns_replace_pending_deadlines() {
        let mut tracker = Tracker::default();
        for (deadline, now) in [(500, 1000), (4000, 1000), (6000, 2000)] {
            tracker.consume(&pickpocket(json!({"cooldownUntilUnixMs":deadline})), now);
        }
        assert!(tracker.tick(4000).is_none());
        assert!(tracker.tick(6000).is_some());
        tracker.consume(&pickpocket(json!({"cooldownUntilUnixMs":8000})), 7000);
        assert!(tracker.tick(8000).is_some());
    }

    #[test]
    fn pickpocket_results_are_announced_once_and_only_when_fresh() {
        let mut tracker = Tracker::default();
        let attempt = |id: &str, ended: u64, outcome: &str| {
            pickpocket(
                json!({"recentAttempts": [{"id": id, "endedAtUnixMs": ended, "outcome": outcome, "itemName": "Wallet"}]}),
            )
        };
        // History present on connect is older than the window: quiet.
        assert!(tracker
            .consume(&attempt("old", 1_000, "Grabbed"), 60_000)
            .is_none());
        let grabbed = tracker
            .consume(&attempt("a", 59_000, "Grabbed"), 60_000)
            .unwrap();
        assert_eq!(
            (grabbed.title, grabbed.tone),
            ("Pickpocket grabbed", Tone::Success)
        );
        assert!(grabbed.detail.contains("Wallet"));
        assert!(tracker
            .consume(&attempt("a", 59_000, "Grabbed"), 60_100)
            .is_none());
        let missed = tracker
            .consume(&attempt("b", 70_000, "Missed"), 70_050)
            .unwrap();
        assert_eq!(missed.tone, Tone::Warning);
        assert!(tracker
            .consume(&attempt("c", 80_000, "Ended"), 80_050)
            .is_none());
    }

    #[test]
    fn pickpocket_pause_and_fault_follow_a_live_run_only() {
        let mut tracker = Tracker::default();
        let state = |state: &str| pickpocket(json!({"state": state, "detail": "why"}));
        // The startup "Waiting" before any tracking is not a pause.
        assert!(tracker.consume(&state("Waiting"), 0).is_none());
        assert!(tracker.consume(&state("Searching"), 0).is_none());
        let paused = tracker.consume(&state("Waiting"), 0).unwrap();
        assert_eq!((paused.title, paused.silent), ("Pickpocket paused", true));
        let faulted = tracker.consume(&state("Faulted"), 0).unwrap();
        assert_eq!(
            (faulted.tone, faulted.detail.as_str()),
            (Tone::Critical, "why")
        );
        assert!(tracker.consume(&state("Faulted"), 0).is_none());
        assert!(tracker.consume(&state("Stopped"), 0).is_none());
    }

    #[test]
    fn fishing_alerts_once_per_cast_and_on_leaving_a_run() {
        let mut tracker = Tracker::default();
        let mut titles = Vec::new();
        for state in [
            "Stopped",
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
            "Stopped",
            "Armed",
            "Faulted",
            "Stopped",
        ] {
            if let Some(notice) = tracker.consume(&fishing(state), 0) {
                titles.push(notice.title);
            }
        }
        assert_eq!(
            titles,
            [
                "Ready for the next catch",
                "Ready for the next catch",
                "Fishing stopped",
                "Fishing stopped on a problem"
            ]
        );
    }

    #[test]
    fn engine_loss_cancels_readiness_and_recovery_is_announced_once() {
        let mut tracker = Tracker::default();
        let bridge = |connected: bool| json!({"name":"bridge_state","payload":{"connected":connected,"detail":"closed"}});
        assert!(tracker.consume(&bridge(true), 0).is_none());
        tracker.consume(&pickpocket(json!({"cooldownUntilUnixMs":4000})), 1000);
        let lost = tracker.consume(&bridge(false), 2000).unwrap();
        assert_eq!(
            (lost.tone, lost.detail.as_str()),
            (Tone::Critical, "closed")
        );
        assert!(tracker.tick(5000).is_none());
        assert_eq!(
            tracker.consume(&bridge(true), 6000).unwrap().tone,
            Tone::Success
        );
        assert!(tracker.consume(&bridge(true), 7000).is_none());
    }

    #[test]
    fn shortcut_confirmations_reflect_the_engine_response() {
        let started = Notice::shortcut(
            "toggle_pickpocket_observe",
            &json!({"pickpocket":{"observing":true,"inputMode":"PrecisionAttempt"}}),
            "Mouse 4",
        )
        .unwrap();
        assert_eq!(started.title, "Pickpocket started");
        assert_eq!(
            started.detail,
            "Armed for one precision tap. Press Mouse 4 again to stop."
        );
        assert_eq!(started.duration_ms, SHORTCUT_DURATION_MS);

        let observe = Notice::shortcut(
            "toggle_pickpocket_observe",
            &json!({"pickpocket":{"observing":true,"inputMode":"Observe"}}),
            "F7",
        )
        .unwrap();
        assert!(observe.detail.starts_with("Watching the minigame."));

        let stopped = Notice::shortcut(
            "toggle_pickpocket_observe",
            &json!({"pickpocket":{"observing":false}}),
            "F7",
        )
        .unwrap();
        assert_eq!(
            (stopped.title, stopped.activity),
            ("Pickpocket stopped", "Pickpocket")
        );

        let fishing = Notice::shortcut("toggle", &json!({"routineState":"Armed"}), "F10").unwrap();
        assert_eq!(
            fishing.detail,
            "Watching for the meter. Press F10 again to stop."
        );
        assert!(Notice::shortcut("toggle", &json!({"routineState":"Stopped"}), "F10").is_none());
        assert!(Notice::shortcut("stop", &json!({}), "Pause")
            .unwrap()
            .interrupts());
        assert!(Notice::shortcut("snapshot", &json!({}), "F1").is_none());
    }

    #[test]
    fn preferences_fill_missing_fields_for_older_files() {
        let loaded: Preferences = serde_json::from_str(r#"{"popups":false,"sound":true}"#).unwrap();
        assert!(!loaded.popups);
        assert!(loaded.shortcuts);
        assert_eq!(loaded.corner, Corner::TopRight);
        let corner: Preferences = serde_json::from_str(r#"{"corner":"bottom-left"}"#).unwrap();
        assert_eq!(corner.corner, Corner::BottomLeft);
    }

    #[test]
    fn unknown_fields_and_values_reset_only_themselves() {
        let loaded =
            parse_preferences(br#"{"popups":false,"sound":"loud","corner":"center","future":1}"#)
                .unwrap();
        assert!(!loaded.popups);
        assert!(loaded.sound);
        assert_eq!(loaded.corner, Corner::TopRight);
        assert!(parse_preferences(b"[]").is_err());
        assert!(parse_preferences(br#"{"popups":"#).is_err());
    }

    #[test]
    fn notifications_json_atomic_write_and_unknown_field() {
        let dir = std::env::temp_dir().join(format!("cuepilot-notif-test-{}", std::process::id()));
        fs::create_dir_all(&dir).unwrap();
        let path = dir.join("notifications.json");
        write_atomic(&path, br#"{"sound":false,"corner":"top-left"}"#).unwrap();
        write_atomic(
            &path,
            br#"{"popups":false,"corner":"diagonal","future":true}"#,
        )
        .unwrap();
        assert!(!path.with_extension("json.tmp").exists());
        let loaded = parse_preferences(&fs::read(&path).unwrap()).unwrap();
        assert!(!loaded.popups);
        assert!(loaded.sound);
        assert_eq!(loaded.corner, Corner::TopRight);
        fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn critical_notice_keeps_cooldown_ready() {
        let mut delivery = ready_delivery();
        delivery.enqueue(Notice::fishing_cast(), 0);
        shown(delivery.advance(SETTLE_MS));
        delivery.tracker.cooldown = Some(1000);
        // The ready alert lands while the Fishing card is still up ...
        assert!(matches!(delivery.advance(1000), Action::Idle));
        // ... then an emergency stop interrupts without dropping it.
        delivery.enqueue(Notice::shortcut("stop", &json!({}), "Pause").unwrap(), 1100);
        assert_eq!(delivery.pending.len(), 2);
        let at = 1100 + SETTLE_MS;
        assert_eq!(shown(delivery.advance(at)).title, "Emergency stop");
        assert_eq!(
            shown(delivery.advance(at + ALERT_DURATION_MS)).title,
            "Ready for another pickpocket"
        );
    }

    #[test]
    fn failed_emergency_stop_shortcut_is_critical() {
        assert!(Notice::shortcut_failed("stop", "engine down").interrupts());
        let toggle = Notice::shortcut_failed("toggle", "engine down");
        assert_eq!(toggle.tone, Tone::Warning);
        assert!(!toggle.interrupts());
    }

    #[test]
    fn tray_hint_is_silent_and_queued_once_per_launch() {
        let mut delivery = Delivery::default();
        delivery.announce_background(0);
        delivery.announce_background(0);
        assert_eq!(delivery.pending.len(), 1);
        let notice = &delivery.pending[0].notice;
        assert_eq!(notice.activity, "Background");
        assert!(notice.silent);
        let payload = serde_json::to_value(notice).unwrap();
        assert!(payload.get("silent").is_none());
        assert_eq!(payload["tone"], "info");
    }

    #[test]
    fn notices_settle_then_same_activity_replaces_in_place() {
        let mut delivery = ready_delivery();
        delivery.enqueue(Notice::pickpocket_ready(), 0);
        assert!(matches!(delivery.advance(SETTLE_MS - 1), Action::Idle));
        assert_eq!(
            shown(delivery.advance(SETTLE_MS)).title,
            "Ready for another pickpocket"
        );
        // A different activity waits for the card to time out ...
        delivery.enqueue(Notice::fishing_cast(), 200);
        assert!(matches!(delivery.advance(1000), Action::Idle));
        // (sleeping meanwhile, not spinning on the settled notice)
        assert_eq!(
            delivery.next_wake(1000),
            Duration::from_millis(IDLE_WAKE_MS)
        );
        // ... but the same activity takes over at once.
        delivery.enqueue(
            Notice::pickpocket_result(&json!({"outcome":"Missed"})).unwrap(),
            300,
        );
        let replaced = shown(delivery.advance(300 + SETTLE_MS));
        assert_eq!(replaced.title, "Pickpocket missed");
        // The waiting card swaps in when the replacement expires, with no hide between.
        let expiry = 300 + SETTLE_MS + DURATION_MS;
        assert_eq!(
            shown(delivery.advance(expiry)).title,
            "Ready for the next catch"
        );
        let end = expiry + DURATION_MS;
        assert!(matches!(delivery.advance(end), Action::Dismiss));
        assert!(matches!(delivery.advance(end + EXIT_MS - 1), Action::Idle));
        assert!(matches!(delivery.advance(end + EXIT_MS), Action::Hide));
        assert!(matches!(delivery.advance(end + EXIT_MS + 1), Action::Idle));
    }

    #[test]
    fn events_landing_together_collapse_and_critical_notices_interrupt() {
        let mut delivery = ready_delivery();
        delivery.enqueue(Notice::fishing_cast(), 0);
        delivery.enqueue(Notice::pickpocket_ready(), 10);
        // Shortcut confirmation 40 ms after the status change it caused: one card.
        let stopped = Notice::shortcut(
            "toggle_pickpocket_observe",
            &json!({"pickpocket":{"observing":false}}),
            "F7",
        )
        .unwrap();
        delivery.enqueue(stopped, 50);
        assert_eq!(delivery.pending.len(), 2);
        assert_eq!(shown(delivery.advance(200)).activity, "Fishing");
        let emergency = Notice::shortcut("stop", &json!({}), "Pause").unwrap();
        delivery.enqueue(emergency, 300);
        assert_eq!(delivery.pending.len(), 1);
        assert_eq!(
            shown(delivery.advance(300 + SETTLE_MS)).title,
            "Emergency stop"
        );
    }

    #[test]
    fn popups_off_hides_the_card_and_still_delivers_sound() {
        let mut delivery = ready_delivery();
        delivery.enqueue(Notice::fishing_cast(), 0);
        shown(delivery.advance(SETTLE_MS));
        delivery.preferences.popups = false;
        assert!(matches!(delivery.advance(SETTLE_MS + 1), Action::Hide));
        delivery.enqueue(Notice::pickpocket_ready(), 1000);
        shown(delivery.advance(1000 + SETTLE_MS));
        assert!(delivery.showing.is_none());
        assert!(matches!(
            delivery.advance(1000 + SETTLE_MS + DURATION_MS),
            Action::Idle
        ));
    }

    #[test]
    fn delivery_sleeps_until_the_next_deadline() {
        let mut delivery = ready_delivery();
        assert_eq!(delivery.next_wake(0), Duration::from_millis(IDLE_WAKE_MS));
        delivery.enqueue(Notice::fishing_cast(), 0);
        assert_eq!(
            delivery.next_wake(20),
            Duration::from_millis(SETTLE_MS - 20)
        );
        shown(delivery.advance(SETTLE_MS));
        delivery.tracker.cooldown = Some(SETTLE_MS + 300);
        assert_eq!(delivery.next_wake(SETTLE_MS), Duration::from_millis(300));
    }

    #[test]
    fn overlay_fits_scaled_and_negative_origin_monitors_in_every_corner() {
        for corner in [
            Corner::TopRight,
            Corner::TopLeft,
            Corner::BottomRight,
            Corner::BottomLeft,
        ] {
            for (x, y, width, height) in [
                (0, 0, 1920, 1040),
                (-2560, -360, 2560, 1400),
                (1920, 48, 800, 560),
            ] {
                for scale in [1.0, 1.25, 1.5, 2.0, 3.0] {
                    let (left, top, w, h) = bounds(x, y, width, height, scale, corner);
                    assert!(left >= x && top >= y);
                    assert!(left + w as i32 <= x + width as i32);
                    assert!(top + h as i32 <= y + height as i32);
                }
            }
        }
        let (left, top, ..) = bounds(0, 0, 1920, 1040, 1.0, Corner::BottomLeft);
        assert_eq!((left, top), (6, 1040 - 6 - 124));
    }
}
