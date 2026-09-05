<script lang="ts">
  import { onMount } from "svelte";
  import { invoke } from "@tauri-apps/api/core";
  import type { PickpocketRecentAttempt } from "../engine.svelte";
  import { attemptReport, outcomeLabel } from "./history";
  let entries = $state<PickpocketRecentAttempt[]>([]);
  let selected = $state<PickpocketRecentAttempt | null>(null);
  let outcome = $state("All");
  let page = $state(0);
  let total = $state(0);
  let pending = $state(false);
  let error = $state("");
  let message = $state("");
  let pageSize = $state(5);
  async function refresh(next = 0) {
    pending = true; error = ""; message = "";
    try {
      const result = await invoke<{ attempts: PickpocketRecentAttempt[]; total: number; page: number; pageSize: number; error?: string }>("engine_command", { command: "pickpocket_history", settings: { page: next, outcome } });
      entries = result.attempts; total = result.total; page = result.page; pageSize = result.pageSize;
      selected = entries[0] ?? null; error = result.error ?? "";
    } catch (failure) { error = `Could not load history: ${String(failure)}`; }
    finally { pending = false; }
  }
  async function copy() {
    if (!selected) return;
    try { await navigator.clipboard.writeText(attemptReport(selected)); message = "Selected attempt report copied."; }
    catch (failure) { error = `Could not copy report: ${String(failure)}`; }
  }
  async function evidence() {
    if (!selected?.sessionId) return;
    try { await invoke("open_evidence_session", { activity: "pickpocket", sessionId: selected.sessionId }); message = "Selected evidence folder opened."; }
    catch (failure) { error = `Evidence unavailable: ${String(failure)}`; }
  }
  onMount(() => { void refresh(); });
</script>

<section class="history" aria-label="Pickpocket history">
  <header><div><h2>Attempt history</h2><p>Last 1,000 attempts · saved across restarts</p></div><label>Outcome <select bind:value={outcome} onchange={() => refresh()} disabled={pending}><option>All</option><option value="Grabbed">Picked up</option><option>Missed</option><option value="Ended">Unknown</option></select></label><button onclick={() => refresh(page)} disabled={pending}>Refresh</button></header>
  <div class="history-body">
    <div class="attempts" aria-label="Saved attempts" aria-busy={pending}>
      {#each entries as entry}<button class:selected={entry.id === selected?.id} aria-pressed={entry.id === selected?.id} onclick={() => { selected = entry; message = ""; }}><span><strong>{entry.itemName ?? "Unknown item"}</strong><small>{new Date(entry.endedAtUnixMs).toLocaleString()} · {entry.color ? `${entry.color} rarity` : "Unknown rarity"}</small></span><span class:miss={entry.outcome === "Missed"}>{outcomeLabel(entry.outcome)}</span></button>{:else}<p class="empty">{pending ? "Loading attempts…" : "No saved attempts match this filter."}</p>{/each}
      <nav aria-label="History pages"><button onclick={() => refresh(page - 1)} disabled={pending || page === 0}>Previous</button><span>{total ? `${page + 1} / ${Math.ceil(total / pageSize)}` : "0 attempts"}</span><button onclick={() => refresh(page + 1)} disabled={pending || (page + 1) * pageSize >= total}>Next</button></nav>
    </div>
    <aside aria-label="Selected attempt">
      {#if selected}<span class="eyebrow">{outcomeLabel(selected.outcome)}</span><h3>{selected.itemName ?? "Unknown item"}</h3><p>Planned target name. Pickup identity is not independently verified.</p>
      <dl><div><dt>Red / yellow advance</dt><dd>{selected.redAdvanceMs ?? "—"} / {selected.yellowAdvanceMs ?? "—"} ms</dd></div><div><dt>Result position</dt><dd>{selected.offsetPixels == null ? "Unknown" : `${Math.abs(selected.offsetPixels).toFixed(1)} px ${selected.offsetPixels > 0 ? "past" : selected.offsetPixels < 0 ? "before" : "from"} center`}</dd></div><div><dt>Engine build</dt><dd>{selected.engineVersion ?? "Unknown"}</dd></div><div><dt>Automatic presses</dt><dd>{selected.automaticPresses} / 1</dd></div></dl><p>Position is visual evidence, not measured input latency.</p><div class="actions"><button onclick={copy}>Copy selected report</button><button onclick={evidence} disabled={!selected.sessionId}>Open evidence</button></div>{:else}<h3>Select an attempt</h3><p>Its settings and evidence appear here.</p>{/if}
    </aside>
  </div>
  <p class:error={!!error} role={error ? "alert" : "status"}>{error || message || "Unknown results stay unknown. Historical timing settings never change when you tune a new attempt."}</p>
</section>

<style>
  .history { padding:18px; border:1px solid var(--line); border-radius:14px; background:var(--panel); }
  header,.actions,nav { display:flex; align-items:center; gap:10px; } header > div { flex:1; } h2,h3 { margin:0 0 6px; font-weight:550; } h2 { font-size:20px; } h3 { font-size:22px; }
  p,small,dt,nav,label { color:var(--text-muted); font-size:11px; line-height:1.5; } p { margin:5px 0; } label { display:flex; gap:8px; align-items:center; }
  button,select { border:1px solid var(--line); border-radius:7px; background:var(--bg); color:var(--text); padding:8px 10px; font:inherit; font-size:11px; cursor:pointer; } button:disabled { opacity:.45; cursor:default; } button:focus-visible,select:focus-visible { outline:2px solid var(--accent); outline-offset:2px; }
  .history-body { display:grid; grid-template-columns:minmax(0,1.2fr) minmax(0,1fr); gap:20px; margin:18px 0; } .attempts > button { width:100%; display:flex; justify-content:space-between; align-items:center; gap:8px; text-align:left; padding:11px; margin-bottom:6px; } strong,small { display:block; } strong { font-size:12px; margin-bottom:3px; } .selected { border-color:var(--accent); } .miss { color:#efb774; } nav { justify-content:space-between; margin-top:12px; } aside { border-left:1px solid var(--line); padding-left:20px; } .eyebrow { display:block; color:var(--accent); font-size:11px; margin-bottom:10px; } dl { margin:15px 0; } dl div { display:flex; justify-content:space-between; gap:8px; padding:7px 0; font-size:11px; } dd { margin:0; text-align:right; } .actions { flex-wrap:wrap; margin-top:15px; } .error { color:#ffabab; } .empty { min-height:190px; padding:10px; }
  @media(max-width:800px) { .history { padding:12px; } .history-body { gap:12px; margin:12px 0; } aside { padding-left:12px; } .attempts > button { padding:8px; } header { flex-wrap:wrap; } }
  @media(max-width:800px) and (max-height:650px) { .history { padding:9px 12px; } .history-body { margin:8px 0 2px; } .attempts > button { padding:5px 8px; margin-bottom:4px; } nav { margin-top:5px; } dl { margin:8px 0; } dl div { padding:5px 0; } .actions { margin-top:8px; } .history > p:last-child:empty { display:none; } }
</style>
