//! Global mouse-button shortcuts.
//!
//! Windows has no `RegisterHotKey` equivalent for mouse buttons, so the shell
//! watches the middle and side buttons through a low-level mouse hook. Only a
//! button that is currently bound to a CuePilot command is swallowed; every
//! other press flows through untouched, and the left/right buttons are never
//! inspected at all.

use std::{
    ptr,
    sync::{
        atomic::{AtomicBool, Ordering},
        OnceLock,
    },
    thread,
};

use tauri::AppHandle;
use windows_sys::Win32::{
    Foundation::{LPARAM, LRESULT, WPARAM},
    System::LibraryLoader::GetModuleHandleW,
    UI::{
        Input::KeyboardAndMouse::{GetAsyncKeyState, VIRTUAL_KEY, VK_CONTROL, VK_MENU, VK_SHIFT},
        WindowsAndMessaging::{
            CallNextHookEx, DispatchMessageW, GetMessageW, SetWindowsHookExW, TranslateMessage,
            HC_ACTION, MSG, MSLLHOOKSTRUCT, WH_MOUSE_LL, WM_MBUTTONDOWN, WM_MBUTTONUP,
            WM_XBUTTONDOWN, WM_XBUTTONUP, XBUTTON1, XBUTTON2,
        },
    },
};

pub(crate) const MOUSE_MIDDLE: &str = "MouseMiddle";
pub(crate) const MOUSE_X1: &str = "MouseX1";
pub(crate) const MOUSE_X2: &str = "MouseX2";

static APP: OnceLock<AppHandle> = OnceLock::new();
/// A swallowed button-down must swallow its matching button-up too, otherwise the
/// game sees a release for a press it never received.
static SWALLOWED: [AtomicBool; 3] = [
    AtomicBool::new(false),
    AtomicBool::new(false),
    AtomicBool::new(false),
];

pub(crate) fn install(app: AppHandle) {
    if APP.set(app).is_err() {
        return;
    }
    let spawned = thread::Builder::new()
        .name("cuepilot-mouse-shortcuts".into())
        .spawn(|| unsafe {
            let hook = SetWindowsHookExW(
                WH_MOUSE_LL,
                Some(hook_proc),
                GetModuleHandleW(ptr::null()),
                0,
            );
            if hook.is_null() {
                eprintln!("mouse shortcuts: SetWindowsHookExW failed; mouse buttons cannot be used as shortcuts");
                return;
            }
            let mut message: MSG = std::mem::zeroed();
            while GetMessageW(&mut message, ptr::null_mut(), 0, 0) > 0 {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        });
    if let Err(error) = spawned {
        eprintln!("mouse shortcuts: hook thread failed to start: {error}");
    }
}

/// Builds the same `Ctrl+Shift+Alt+MouseX1` text the bridge stores for a binding.
pub(crate) fn shortcut_text(button: &str, control: bool, shift: bool, alt: bool) -> String {
    let mut parts = Vec::with_capacity(4);
    if control {
        parts.push("Ctrl");
    }
    if shift {
        parts.push("Shift");
    }
    if alt {
        parts.push("Alt");
    }
    parts.push(button);
    parts.join("+")
}

fn button_index(button: &str) -> usize {
    match button {
        MOUSE_MIDDLE => 0,
        MOUSE_X1 => 1,
        _ => 2,
    }
}

unsafe fn pressed(key: VIRTUAL_KEY) -> bool {
    (GetAsyncKeyState(i32::from(key)) as u16) & 0x8000 != 0
}

unsafe fn xbutton(lparam: LPARAM) -> Option<&'static str> {
    let info = &*(lparam as *const MSLLHOOKSTRUCT);
    match (info.mouseData >> 16) as u16 {
        XBUTTON1 => Some(MOUSE_X1),
        XBUTTON2 => Some(MOUSE_X2),
        _ => None,
    }
}

unsafe extern "system" fn hook_proc(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    if code == HC_ACTION as i32 {
        let message = wparam as u32;
        let (button, down) = match message {
            WM_MBUTTONDOWN => (Some(MOUSE_MIDDLE), true),
            WM_MBUTTONUP => (Some(MOUSE_MIDDLE), false),
            WM_XBUTTONDOWN => (xbutton(lparam), true),
            WM_XBUTTONUP => (xbutton(lparam), false),
            _ => (None, false),
        };
        if let Some(button) = button {
            let slot = &SWALLOWED[button_index(button)];
            if down {
                if let Some(app) = APP.get() {
                    let shortcut = shortcut_text(
                        button,
                        pressed(VK_CONTROL),
                        pressed(VK_SHIFT),
                        pressed(VK_MENU),
                    );
                    if crate::dispatch_mouse_shortcut(app, &shortcut) {
                        slot.store(true, Ordering::Relaxed);
                        return 1;
                    }
                }
            } else if slot.swap(false, Ordering::Relaxed) {
                return 1;
            }
        }
    }
    CallNextHookEx(ptr::null_mut(), code, wparam, lparam)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shortcut_text_matches_bridge_shape() {
        assert_eq!(shortcut_text(MOUSE_X1, false, false, false), "MouseX1");
        assert_eq!(
            shortcut_text(MOUSE_X2, true, false, true),
            "Ctrl+Alt+MouseX2"
        );
        assert_eq!(
            shortcut_text(MOUSE_MIDDLE, true, true, true),
            "Ctrl+Shift+Alt+MouseMiddle"
        );
    }
}
