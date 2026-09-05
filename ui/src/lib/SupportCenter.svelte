<script lang="ts">
  import { onMount } from "svelte";
  import { invoke } from "@tauri-apps/api/core";
  let { connected, engineVersion, initialActivity = "pickpocket" }: { connected: boolean; engineVersion?: string; initialActivity?: string } = $props();
  let activity = $state("pickpocket");
  let sessions = $state<{ id: string; complete: boolean; bytes: number; sizeLimited: boolean; engineVersion?: string }[]>([]);
  let page = $state(0), total = $state(0), busy = $state(false), error = $state(""), message = $state("");
  let totalBytes = $state(0), storageLimited = $state(false);
  let report = $state<{ sessionId: string; report: string; decisions: string[] } | null>(null);
  let health = $state<{ shellVersion: string; development: boolean; processId: number; diagnosticsAvailable: boolean; logBytes: number; logLimited: boolean; protocolVersion: number } | null>(null);
  async function load(next = 0) {
    busy = true; error = ""; report = null;
    try {
      const result = await invoke<{ sessions: typeof sessions; total: number; page: number; totalBytes: number; storageLimited: boolean }>("support_sessions", { activity, page: next });
      sessions = result.sessions; total = result.total; page = result.page; totalBytes = result.totalBytes; storageLimited = result.storageLimited;
      health = await invoke("support_health");
    } catch (failure) { error = String(failure); } finally { busy = false; }
  }
  async function select(sessionId: string) {
    busy = true; error = "";
    try { report = await invoke("support_report", { activity, sessionId }); }
    catch (failure) { error = String(failure); } finally { busy = false; }
  }
  async function copyBuild() {
    try { await navigator.clipboard.writeText(JSON.stringify({ ...health, connected, engineVersion: engineVersion ?? "Unknown" }, null, 2)); message = "Build and health details copied."; }
    catch (failure) { error = String(failure); }
  }
  async function exportReport() {
    if (!report) return;
    try { const result = await invoke<string>("export_evidence_report", { activity, sessionId: report.sessionId }); message = result; }
    catch (failure) { error = String(failure); }
  }
  onMount(() => { activity = initialActivity; void load(); });
</script>
<section class="support" aria-label="Build and session diagnostics">
  <h3>Build & health</h3>
  <p>{health?.development ? "Development" : "Installed / packaged"} shell {health?.shellVersion ?? "…"} · engine {engineVersion ?? "Unknown"} · {connected ? "Connected" : "Disconnected"}</p>
  <p>Protocol {health?.protocolVersion ?? "…"} · local log {Math.ceil((health?.logBytes ?? 0) / 1024)} KB{health?.logLimited ? " · size limit reached" : ""}</p>
  <button onclick={copyBuild} disabled={!health}>Copy build details</button>
  <p class="muted">Passive status. Capture is not started by this panel.</p>
  <header><h3>Recorded sessions</h3><select aria-label="Diagnostic activity" bind:value={activity} onchange={() => load()} disabled={busy}><option value="pickpocket">Pickpocket</option><option value="fishing">Fishing</option><option value="lockpicking">Lockpicking</option></select><button onclick={() => load(page)} disabled={busy}>Refresh</button></header><p class="muted">{total} sessions · {(totalBytes / 1048576).toFixed(1)} MB{storageLimited ? "+ (scan bounded)" : ""}. Active, incomplete and saved evidence remain visible; cleanup is never automatic here.</p>
  <div aria-busy={busy}>{#each sessions as session}<button class="session" class:selected={report?.sessionId === session.id} onclick={() => select(session.id)} disabled={busy}><span>{session.id}<small>{session.engineVersion ?? "Unknown build"} · {(session.bytes / 1048576).toFixed(1)} MB{session.sizeLimited ? "+" : ""}</small></span><span>{session.complete ? "Finalized" : "Active / incomplete"}</span></button>{:else}<p>No sessions recorded for this activity.</p>{/each}</div>
  <nav aria-label="Session pages"><button disabled={busy || page === 0} onclick={() => load(page - 1)}>Previous</button><span>{total ? `${page + 1} / ${Math.ceil(total / 5)}` : "0 sessions"}</span><button disabled={busy || (page + 1) * 5 >= total} onclick={() => load(page + 1)}>Next</button></nav>
  {#if report}<h3>Decision timeline</h3><p class="muted">First 30 changes from a bounded trace excerpt. Missing decisions may reflect recording limits.</p><ol>{#each report.decisions as decision}<li>{decision}</li>{:else}<li>No readable decision events in this excerpt.</li>{/each}</ol><details><summary>Session report</summary><pre>{report.report}</pre></details><button onclick={exportReport}>Export text evidence bundle</button><p class="muted">Local report and manifest only. Screenshots stay in the original evidence folder.</p>{/if}
  <p class:error={!!error} role={error ? "alert" : "status"}>{error || message}</p>
</section>
<style>
  .support { padding:0 0 20px; } h3 { font-size:16px; margin:14px 0 8px; } p,li,nav { font-size:12px; line-height:1.5; } header,nav { display:flex; gap:10px; align-items:center; justify-content:space-between; } button,select { background:var(--panel); border:1px solid var(--line); border-radius:7px; color:var(--text); font:inherit; font-size:11px; padding:8px; cursor:pointer; } button:disabled { opacity:.5; } button:focus-visible,select:focus-visible { outline:2px solid var(--accent); outline-offset:2px; } .session { display:flex; width:100%; justify-content:space-between; text-align:left; gap:12px; margin:6px 0; overflow-wrap:anywhere; } .selected { border-color:var(--accent); } small { display:block; color:var(--text-muted); margin-top:5px; } .muted { color:var(--text-muted); font-size:11px; } pre { white-space:pre-wrap; overflow-wrap:anywhere; font-size:11px; max-height:300px; overflow:auto; } details { margin:12px 0; } ol { max-height:240px; overflow:auto; padding-left:22px; } li { padding:4px; } .error { color:#ffabab; }
</style>
