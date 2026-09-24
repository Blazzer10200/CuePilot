//! Notification-area icon. Closing the main window only hides it, so the engine,
//! global shortcuts and popups keep running; this icon is how the user brings
//! the window back or really quits.
use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    AppHandle, Manager,
};

use crate::engine_bridge::EngineBridge;

const OPEN_ID: &str = "open";
const QUIT_ID: &str = "quit";

fn tooltip(identifier: &str) -> &'static str {
    if identifier.ends_with(".dev") {
        "CuePilot Dev"
    } else {
        "CuePilot"
    }
}

pub(crate) fn setup(app: &mut tauri::App) -> tauri::Result<()> {
    let open = MenuItem::with_id(app, OPEN_ID, "Open CuePilot", true, None::<&str>)?;
    let quit = MenuItem::with_id(app, QUIT_ID, "Quit CuePilot", true, None::<&str>)?;
    let separator = PredefinedMenuItem::separator(app)?;
    let menu = Menu::with_items(app, &[&open, &separator, &quit])?;

    let mut builder = TrayIconBuilder::with_id("main")
        .tooltip(tooltip(&app.config().identifier))
        .menu(&menu)
        // Left click opens the window like other tray apps; the menu is on right click.
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id().as_ref() {
            OPEN_ID => crate::focus_main_window(app),
            QUIT_ID => quit_application(app),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                crate::focus_main_window(tray.app_handle());
            }
        });
    match app.default_window_icon().cloned() {
        Some(icon) => builder = builder.icon(icon),
        None => crate::support::log("tray", "no window icon; tray icon will be blank"),
    }
    builder.build(app)?;
    Ok(())
}

/// The only user-facing way out now that the window close button hides.
/// Stops the owned engine first, the same order the updater uses.
pub(crate) fn quit_application(app: &AppHandle) {
    app.state::<EngineBridge>().shutdown(app);
    app.exit(0);
}

#[cfg(test)]
mod tests {
    use super::tooltip;

    #[test]
    fn tooltip_names_the_dev_profile() {
        assert_eq!(tooltip("com.blazzer.cuepilot"), "CuePilot");
        assert_eq!(tooltip("com.blazzer.cuepilot.dev"), "CuePilot Dev");
    }
}
