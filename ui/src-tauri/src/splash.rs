//! Pre-WebView launch splash.
//!
//! The shell spends roughly five seconds inside Tauri/WebView2 window creation
//! before `setup()` runs, and nothing HTML-based can paint during that window
//! because the browser engine that would render it does not exist yet. This is
//! a plain GDI window instead: it owns no web content, so it appears in about a
//! tenth of a second and covers the otherwise blank main window.
//!
//! It never takes focus, it closes as soon as the real UI reports itself alive,
//! and it self-destructs on a timer so a failed launch can never strand it on
//! screen.

#![cfg(windows)]

use std::sync::atomic::{AtomicBool, AtomicI32, AtomicIsize, Ordering};

use windows_sys::Win32::Foundation::{COLORREF, HWND, LPARAM, LRESULT, RECT, WPARAM};
use windows_sys::Win32::Graphics::Gdi::{
    BeginPaint, BitBlt, CreateCompatibleBitmap, CreateCompatibleDC, CreateFontW, CreateSolidBrush,
    DeleteDC, DeleteObject, DrawTextW, EndPaint, FillRect, InvalidateRect, SelectObject, SetBkMode,
    SetTextColor, CLEARTYPE_QUALITY, DEFAULT_CHARSET, DT_LEFT, DT_SINGLELINE, DT_TOP, FF_DONTCARE,
    FW_NORMAL, FW_SEMIBOLD, HBRUSH, HDC, OUT_DEFAULT_PRECIS, PAINTSTRUCT, SRCCOPY, TRANSPARENT,
};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::HiDpi::GetDpiForSystem;
use windows_sys::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW, GetMessageW,
    GetSystemMetrics, KillTimer, PostMessageW, PostQuitMessage, RegisterClassW, SetTimer,
    SetWindowPos, ShowWindow, TranslateMessage, CS_HREDRAW, CS_VREDRAW, HWND_TOPMOST, MSG,
    SM_CXSCREEN, SM_CYSCREEN, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE, SW_SHOWNOACTIVATE, WM_CLOSE,
    WM_DESTROY, WM_PAINT, WM_TIMER, WNDCLASSW, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW, WS_POPUP,
};

/// Splash geometry in layout units, scaled to the system DPI at creation.
const WIDTH: i32 = 360;
const HEIGHT: i32 = 148;

/// Theme tokens from `ui/src/app.css`, as GDI `0x00BBGGRR` literals.
const BG: COLORREF = 0x001D_1F1F; // --bg          #1f1f1d
const BORDER: COLORREF = 0x0029_2B2B; // --line over --bg
const TRACK: COLORREF = 0x0024_2626; // progress track
const ACCENT: COLORREF = 0x0057_77D9; // --accent      #d97757
const TEXT: COLORREF = 0x00EA_ECEC; // --text        #ececea
const TEXT_MUTED: COLORREF = 0x0094_9A9B; // --text-muted  #9b9a94

const ANIMATION_TIMER: usize = 1;
const LIFETIME_TIMER: usize = 2;
/// Frame interval. The bar is the only moving element, so this is cheap.
const FRAME_MS: u32 = 16;
/// Hard ceiling. A launch that never reaches the UI must not leave a window behind.
const LIFETIME_MS: u32 = 30_000;

/// Live splash window, or 0. Written by the splash thread, read by `hide`.
static WINDOW: AtomicIsize = AtomicIsize::new(0);
/// Set by `hide`. Suppresses a splash that has not finished appearing yet.
static CANCELLED: AtomicBool = AtomicBool::new(false);
/// System DPI captured at creation, for scaling every drawn dimension.
static DPI: AtomicI32 = AtomicI32::new(96);
/// Animation phase in frames, owned by the splash thread.
static PHASE: AtomicIsize = AtomicIsize::new(0);

fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Scales a layout unit to physical pixels.
fn px(value: i32) -> i32 {
    value * DPI.load(Ordering::Relaxed) / 96
}

/// Shows the splash on its own thread with its own message pump.
///
/// Returns immediately. Safe to call when a splash is already up: the second
/// call is ignored rather than stacking a window.
pub(crate) fn show() {
    if WINDOW.load(Ordering::Acquire) != 0 {
        return;
    }
    std::thread::Builder::new()
        .name("cuepilot-splash".into())
        .spawn(|| {
            // A second instance exits inside plugin initialization, well under
            // this delay, so it never flashes a splash it is about to discard.
            std::thread::sleep(std::time::Duration::from_millis(150));
            unsafe { run() };
        })
        .ok();
}

/// Asks the splash to close. Idempotent, and safe to call from any thread.
///
/// Also covers the case where the UI beats the splash to the screen: the flag
/// keeps a window that has not been created yet from ever appearing.
pub(crate) fn hide() {
    CANCELLED.store(true, Ordering::Release);
    let handle = WINDOW.swap(0, Ordering::AcqRel);
    if handle != 0 {
        // WM_CLOSE, not WM_DESTROY: the default handler is what actually calls
        // DestroyWindow. Posting WM_DESTROY would end the message loop while
        // leaving the window on screen.
        unsafe { PostMessageW(handle as HWND, WM_CLOSE, 0, 0) };
    }
}

unsafe fn run() {
    if CANCELLED.load(Ordering::Acquire) {
        return;
    }
    DPI.store(GetDpiForSystem() as i32, Ordering::Relaxed);

    let class_name = wide("CuePilotSplash");
    let instance = GetModuleHandleW(std::ptr::null());

    let class = WNDCLASSW {
        style: CS_HREDRAW | CS_VREDRAW,
        lpfnWndProc: Some(window_proc),
        cbClsExtra: 0,
        cbWndExtra: 0,
        hInstance: instance as _,
        hIcon: std::ptr::null_mut(),
        hCursor: std::ptr::null_mut(),
        hbrBackground: std::ptr::null_mut(),
        lpszMenuName: std::ptr::null(),
        lpszClassName: class_name.as_ptr(),
    };
    // A zero atom means the class already exists from an earlier splash, which
    // is fine; CreateWindowExW below resolves it by name either way.
    RegisterClassW(&class);

    let width = px(WIDTH);
    let height = px(HEIGHT);
    let x = (GetSystemMetrics(SM_CXSCREEN) - width) / 2;
    let y = (GetSystemMetrics(SM_CYSCREEN) - height) / 2;
    let title = wide("CuePilot");

    let window = CreateWindowExW(
        // No taskbar button, and never steal focus: the splash may appear while
        // a full-screen game holds the foreground.
        WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        class_name.as_ptr(),
        title.as_ptr(),
        WS_POPUP,
        x,
        y,
        width,
        height,
        std::ptr::null_mut(),
        std::ptr::null_mut(),
        instance as _,
        std::ptr::null(),
    );
    if window.is_null() {
        return;
    }

    // Re-check after creation: `hide` may have run while the window was being
    // built, in which case it never becomes visible.
    if CANCELLED.load(Ordering::Acquire) {
        DestroyWindow(window);
        return;
    }
    WINDOW.store(window as isize, Ordering::Release);

    // `hide` may have landed between the check above and that store, in which
    // case it saw 0 and posted nothing. Re-check, and claim the handle so only
    // one side ever destroys the window.
    if CANCELLED.load(Ordering::Acquire) && WINDOW.swap(0, Ordering::AcqRel) != 0 {
        DestroyWindow(window);
        return;
    }

    // Above the blank main window Tauri has already created, but without
    // activating it.
    SetWindowPos(
        window,
        HWND_TOPMOST,
        0,
        0,
        0,
        0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
    );
    ShowWindow(window, SW_SHOWNOACTIVATE);
    SetTimer(window, ANIMATION_TIMER, FRAME_MS, None);
    SetTimer(window, LIFETIME_TIMER, LIFETIME_MS, None);

    let mut message: MSG = std::mem::zeroed();
    while GetMessageW(&mut message, std::ptr::null_mut(), 0, 0) > 0 {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    WINDOW.store(0, Ordering::Release);
}

unsafe extern "system" fn window_proc(
    window: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match message {
        WM_TIMER => {
            match wparam {
                ANIMATION_TIMER => {
                    PHASE.fetch_add(1, Ordering::Relaxed);
                    // No background erase; every frame is fully redrawn from
                    // the back buffer.
                    InvalidateRect(window, std::ptr::null(), 0);
                }
                // The launch never completed. Leave nothing on screen.
                LIFETIME_TIMER => {
                    DestroyWindow(window);
                }
                _ => {}
            }
            0
        }
        WM_PAINT => {
            let mut paint: PAINTSTRUCT = std::mem::zeroed();
            let hdc = BeginPaint(window, &mut paint);
            paint_splash(hdc);
            EndPaint(window, &paint);
            0
        }
        WM_DESTROY => {
            KillTimer(window, ANIMATION_TIMER);
            KillTimer(window, LIFETIME_TIMER);
            WINDOW.store(0, Ordering::Release);
            PostQuitMessage(0);
            0
        }
        _ => DefWindowProcW(window, message, wparam, lparam),
    }
}

/// Draws one frame through a back buffer, so the moving bar does not flicker.
unsafe fn paint_splash(hdc: HDC) {
    let width = px(WIDTH);
    let height = px(HEIGHT);

    let memory = CreateCompatibleDC(hdc);
    let bitmap = CreateCompatibleBitmap(hdc, width, height);
    let previous = SelectObject(memory, bitmap as _);

    fill(memory, 0, 0, width, height, BORDER);
    fill(memory, 1, 1, width - 2, height - 2, BG);

    draw_text(memory, "CuePilot", px(28), px(30), 20, FW_SEMIBOLD, TEXT);
    draw_text(
        memory,
        "Starting\u{2026}",
        px(28),
        px(62),
        13,
        FW_NORMAL,
        TEXT_MUTED,
    );

    // Indeterminate bar: a fixed-width accent segment sweeping a dark track,
    // easing at both ends so it reads as motion rather than a wrapping jump.
    let track_x = px(28);
    let track_y = px(100);
    let track_w = width - px(56);
    let track_h = px(4).max(2);
    fill(memory, track_x, track_y, track_w, track_h, TRACK);

    let segment = track_w / 3;
    let period = 110_i64;
    let phase = PHASE.load(Ordering::Relaxed) as i64 % period;
    let progress = phase as f64 / period as f64;
    // Smoothstep out and back, so the segment slows at each end.
    let eased = if progress < 0.5 {
        let t = progress * 2.0;
        t * t * (3.0 - 2.0 * t)
    } else {
        let t = (1.0 - progress) * 2.0;
        t * t * (3.0 - 2.0 * t)
    };
    let offset = (eased * (track_w - segment) as f64).round() as i32;
    fill(memory, track_x + offset, track_y, segment, track_h, ACCENT);

    BitBlt(hdc, 0, 0, width, height, memory, 0, 0, SRCCOPY);
    SelectObject(memory, previous);
    DeleteObject(bitmap as _);
    DeleteDC(memory);
}

unsafe fn fill(hdc: HDC, x: i32, y: i32, width: i32, height: i32, color: COLORREF) {
    let rect = RECT {
        left: x,
        top: y,
        right: x + width,
        bottom: y + height,
    };
    let brush: HBRUSH = CreateSolidBrush(color);
    FillRect(hdc, &rect, brush);
    DeleteObject(brush as _);
}

/// `size` is a layout unit; it is DPI-scaled here like every other dimension.
unsafe fn draw_text(hdc: HDC, text: &str, x: i32, y: i32, size: i32, weight: u32, color: COLORREF) {
    let scaled = px(size);
    let face = wide("Segoe UI Variable Text");
    let font = CreateFontW(
        -scaled,
        0,
        0,
        0,
        weight as i32,
        0,
        0,
        0,
        DEFAULT_CHARSET.into(),
        OUT_DEFAULT_PRECIS.into(),
        // Default clipping and pitch; ClearType matches the shell's own text.
        0,
        CLEARTYPE_QUALITY.into(),
        FF_DONTCARE as u32,
        face.as_ptr(),
    );
    let previous = SelectObject(hdc, font as _);
    SetBkMode(hdc, TRANSPARENT as i32);
    SetTextColor(hdc, color);

    let mut rect = RECT {
        left: x,
        top: y,
        right: px(WIDTH) - px(20),
        bottom: y + scaled * 2,
    };
    let mut buffer = wide(text);
    DrawTextW(
        hdc,
        buffer.as_mut_ptr(),
        -1,
        &mut rect,
        DT_LEFT | DT_TOP | DT_SINGLELINE,
    );

    SelectObject(hdc, previous);
    DeleteObject(font as _);
}
