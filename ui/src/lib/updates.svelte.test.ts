import { beforeEach, describe, expect, it, vi } from "vitest";

const tauri = vi.hoisted(() => ({
  invoke: vi.fn(),
  listen: vi.fn(),
  unlisten: vi.fn(),
  progress: undefined as ((event: { payload: number }) => void) | undefined,
}));

vi.mock("@tauri-apps/api/core", () => ({ invoke: tauri.invoke }));
vi.mock("@tauri-apps/api/event", () => ({ listen: tauri.listen }));

import { UpdateClient } from "./updates.svelte";

beforeEach(() => {
  tauri.invoke.mockReset();
  tauri.listen.mockReset();
  tauri.unlisten.mockReset();
  tauri.progress = undefined;
  tauri.listen.mockImplementation(async (_name, handler) => {
    tauri.progress = handler;
    return tauri.unlisten;
  });
});

describe("UpdateClient", () => {
  it("keeps source-built development copies out of the release feed", async () => {
    tauri.invoke.mockResolvedValue({ installed: false, development: true, currentVersion: "5.1.8", detail: "Development build" });
    const client = new UpdateClient();

    await client.initialize();

    expect(client.state).toBe("disabled");
    expect(tauri.invoke).toHaveBeenCalledTimes(1);
    expect(tauri.invoke).toHaveBeenCalledWith("updater_status");
    client.dispose();
  });

  it("surfaces an available installed release", async () => {
    tauri.invoke
      .mockResolvedValueOnce({ installed: true, development: false, currentVersion: "5.1.8", detail: "Ready" })
      .mockResolvedValueOnce({ version: "5.2.0", releaseName: "CuePilot-5.2.0-full.nupkg", sizeBytes: 42_000_000, notesMarkdown: "Safer updates" });
    const client = new UpdateClient();

    await client.initialize();

    expect(client.state).toBe("available");
    expect(client.info?.version).toBe("5.2.0");
    expect(client.sizeLabel).toBe("40.1 MB");
    client.dispose();
  });

  it("downloads, reports progress, and hands off to apply", async () => {
    const client = new UpdateClient();
    client.state = "available";
    client.info = { version: "5.2.0", releaseName: "full.nupkg", sizeBytes: 1, notesMarkdown: "" };
    tauri.invoke.mockImplementation(async (command: string) => {
      if (command === "download_update") tauri.progress?.({ payload: 64 });
    });

    await client.install();

    expect(client.state).toBe("installing");
    expect(client.progress).toBe(100);
    expect(tauri.invoke.mock.calls.map(([command]) => command)).toEqual(["download_update", "apply_pending_update"]);
    expect(tauri.unlisten).toHaveBeenCalledTimes(1);
  });

  it("returns to an actionable available state after a failed download", async () => {
    const client = new UpdateClient();
    client.state = "available";
    client.info = { version: "5.2.0", releaseName: "full.nupkg", sizeBytes: 1, notesMarkdown: "" };
    tauri.invoke.mockRejectedValueOnce("network unavailable");

    await client.install();

    expect(client.state).toBe("available");
    expect(client.error).toContain("network unavailable");
    expect(client.dialogOpen).toBe(true);
  });
});
