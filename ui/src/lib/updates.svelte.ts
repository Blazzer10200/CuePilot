import { invoke } from "@tauri-apps/api/core";
import { listen, type UnlistenFn } from "@tauri-apps/api/event";

export type UpdateState =
  | "idle"
  | "disabled"
  | "checking"
  | "uptodate"
  | "available"
  | "downloading"
  | "installing"
  | "error";

export interface UpdaterRuntime {
  installed: boolean;
  development: boolean;
  currentVersion: string;
  detail: string;
}

export interface UpdateInfo {
  version: string;
  releaseName: string;
  sizeBytes: number;
  notesMarkdown: string;
}

const SIX_HOURS = 6 * 60 * 60 * 1000;

export class UpdateClient {
  state = $state<UpdateState>("idle");
  runtime = $state<UpdaterRuntime | null>(null);
  info = $state<UpdateInfo | null>(null);
  progress = $state(0);
  error = $state<string | null>(null);
  dialogOpen = $state(false);
  dismissed = $state(false);
  private initialized = false;
  private timer: ReturnType<typeof setInterval> | null = null;

  get hasUpdate() {
    return this.state === "available" || this.state === "downloading" || this.state === "installing";
  }

  get sizeLabel() {
    const bytes = this.info?.sizeBytes ?? 0;
    if (bytes <= 0) return "";
    const megabytes = bytes / (1024 * 1024);
    return megabytes >= 1 ? `${megabytes.toFixed(1)} MB` : `${Math.round(bytes / 1024)} KB`;
  }

  async initialize() {
    if (this.initialized) return;
    this.initialized = true;
    try {
      this.runtime = await invoke<UpdaterRuntime>("updater_status");
      if (!this.runtime.installed) {
        this.state = "disabled";
        return;
      }
      await this.refresh();
      this.timer = setInterval(() => {
        if (!["downloading", "installing", "checking"].includes(this.state)) void this.refresh();
      }, SIX_HOURS);
    } catch (error) {
      this.error = String(error);
      this.state = "error";
    }
  }

  dispose() {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    this.initialized = false;
  }

  async refresh() {
    if (this.state === "downloading" || this.state === "installing") return;
    this.state = "checking";
    this.error = null;
    try {
      const update = await invoke<UpdateInfo | null>("check_for_updates");
      this.info = update;
      this.state = update ? "available" : "uptodate";
      if (update) this.dismissed = false;
    } catch (error) {
      this.error = String(error);
      this.state = "error";
    }
  }

  async install() {
    if (this.state !== "available") return;
    this.error = null;
    this.progress = 0;
    this.state = "downloading";
    let unlisten: UnlistenFn | null = null;
    try {
      unlisten = await listen<number>("update-progress", (event) => {
        this.progress = Math.min(100, Math.max(0, event.payload));
      });
      await invoke("download_update");
      this.progress = 100;
      this.state = "installing";
      await invoke("apply_pending_update");
    } catch (error) {
      this.error = String(error);
      this.state = "available";
      this.dialogOpen = true;
    } finally {
      unlisten?.();
    }
  }

  async openReleases() {
    try {
      await invoke("open_update_releases");
    } catch (error) {
      this.error = String(error);
    }
  }

  open() {
    this.dialogOpen = true;
    this.dismissed = true;
  }

  close() {
    if (this.state === "installing") return;
    this.dialogOpen = false;
  }

  dismiss() {
    this.dismissed = true;
  }
}

export const updates = new UpdateClient();
