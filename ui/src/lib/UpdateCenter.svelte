<script lang="ts">
  import { tick } from "svelte";
  import { fade, fly } from "svelte/transition";
  import {
    AlertTriangle,
    CheckCircle2,
    Download,
    ExternalLink,
    RefreshCw,
    ShieldCheck,
    X,
  } from "@lucide/svelte";
  import { updates } from "./updates.svelte";

  let { automationActive = false }: { automationActive?: boolean } = $props();
  let dialog = $state<HTMLDivElement | null>(null);
  let returnFocus: HTMLElement | null = null;
  let wasOpen = false;

  const notes = $derived(
    (updates.info?.notesMarkdown ?? "")
      .split(/\r?\n/)
      .map((line) => line.trim().replace(/^#{1,6}\s+/, "").replace(/^[-*+]\s+/, ""))
      .filter(Boolean)
      .slice(0, 12),
  );

  $effect(() => {
    const open = updates.dialogOpen;
    if (open && !wasOpen) {
      returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      void tick().then(() => dialog?.focus());
    } else if (!open && wasOpen) {
      void tick().then(() => returnFocus?.focus());
    }
    wasOpen = open;
  });

  function onKeydown(event: KeyboardEvent) {
    if (!updates.dialogOpen) return;
    if (event.key === "Escape") {
      event.preventDefault();
      updates.close();
      return;
    }
    if (event.key !== "Tab" || !dialog) return;
    const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(
      'button:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])',
    )).filter((element) => element.getClientRects().length > 0);
    if (!focusable.length) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }
</script>

<svelte:window onkeydown={onKeydown} />

{#if updates.state === "available" && !updates.dismissed && !updates.dialogOpen}
  <aside class="update-banner" aria-label="CuePilot update available" transition:fly={{ y: -8, duration: 180 }}>
    <span><Download size={14} /></span>
    <p><strong>CuePilot v{updates.info?.version} is ready</strong><small>{updates.sizeLabel || "Update"} · installs after confirmation</small></p>
    <button type="button" class="review" onclick={() => updates.open()}>Review</button>
    <button type="button" class="dismiss" aria-label="Dismiss update notice" onclick={() => updates.dismiss()}><X size={13} /></button>
  </aside>
{/if}

{#if updates.dialogOpen}
  <div class="update-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && updates.close()} transition:fade={{ duration: 120 }}>
    <div class="update-dialog" role="dialog" aria-modal="true" aria-labelledby="update-title" tabindex="-1" bind:this={dialog} transition:fly={{ y: 8, duration: 180 }}>
      <header>
        <div><span>RELEASE CHANNEL</span><h2 id="update-title">CuePilot updates</h2></div>
        <button type="button" aria-label="Close updates" onclick={() => updates.close()} disabled={updates.state === "installing"}><X size={16} /></button>
      </header>

      <section class="update-hero" data-state={updates.state}>
        {#if updates.state === "checking"}
          <RefreshCw size={24} class="spin" />
          <h3>Checking for updates…</h3>
          <p>Reading CuePilot's public release feed.</p>
        {:else if updates.state === "available"}
          <Download size={24} />
          <h3>Version {updates.info?.version} is available</h3>
          <p>{updates.sizeLabel ? `${updates.sizeLabel} download. ` : ""}CuePilot will close, install, and relaunch.</p>
        {:else if updates.state === "downloading"}
          <strong class="progress-number">{updates.progress}%</strong>
          <h3>Downloading version {updates.info?.version}</h3>
          <div class="progress-track" role="progressbar" aria-valuenow={updates.progress} aria-valuemin="0" aria-valuemax="100"><i style={`width: ${updates.progress}%`}></i></div>
        {:else if updates.state === "installing"}
          <RefreshCw size={24} class="spin" />
          <h3>Installing safely…</h3>
          <p>The local engine is stopping before CuePilot relaunches.</p>
        {:else if updates.state === "uptodate"}
          <CheckCircle2 size={24} />
          <h3>You're up to date</h3>
          <p>No newer public update available. Installed version: {updates.runtime?.currentVersion ?? __APP_VERSION__}.</p>
        {:else if updates.state === "disabled" && updates.runtime?.development}
          <ShieldCheck size={24} />
          <h3>Development build</h3>
          <p>This copy is updated from source. Installed CuePilot releases use Velopack automatically.</p>
        {:else}
          <AlertTriangle size={24} />
          <h3>{updates.state === "disabled" ? "Updater needs repair" : "Update check failed"}</h3>
          <p>{updates.error ?? updates.runtime?.detail ?? "The release feed is unavailable."}</p>
        {/if}
      </section>

      {#if updates.state === "available" && notes.length}
        <section class="release-notes" aria-label="Release notes">
          <span>WHAT'S NEW</span>
          <ul>{#each notes as line}<li>{line}</li>{/each}</ul>
        </section>
      {/if}

      {#if updates.state === "available" && automationActive}
        <p class="active-warning"><AlertTriangle size={14} /> Stop the active activity before installing this update.</p>
      {/if}
      {#if updates.error && updates.state === "available"}
        <p class="active-warning"><AlertTriangle size={14} /> {updates.error}</p>
      {/if}

      <footer>
        <button type="button" class="link" onclick={() => void updates.openReleases()}><ExternalLink size={13} /> Releases</button>
        <div></div>
        {#if updates.state === "available"}
          <button type="button" onclick={() => updates.close()}>Later</button>
          <button type="button" class="primary" disabled={automationActive} onclick={() => void updates.install()}><Download size={13} /> Update now</button>
        {:else if updates.state === "error"}
          <button type="button" onclick={() => void updates.refresh()}><RefreshCw size={13} /> Try again</button>
          <button type="button" class="primary" onclick={() => updates.close()}>Close</button>
        {:else if updates.state === "disabled" && !updates.runtime?.development}
          <button type="button" class="primary" onclick={() => void updates.openReleases()}><ExternalLink size={13} /> Reinstall</button>
        {:else if updates.state !== "downloading" && updates.state !== "installing" && updates.state !== "checking"}
          <button type="button" onclick={() => void updates.refresh()} disabled={!updates.runtime?.installed}><RefreshCw size={13} /> Check now</button>
          <button type="button" class="primary" onclick={() => updates.close()}>Done</button>
        {:else}
          <button type="button" class="primary" disabled><RefreshCw size={13} class="spin" /> Please wait</button>
        {/if}
      </footer>
    </div>
  </div>
{/if}

<style>
  .update-banner {
    position: fixed;
    z-index: 40;
    top: 66px;
    left: 50%;
    transform: translateX(-50%);
    width: min(470px, calc(100vw - 28px));
    min-height: 54px;
    padding: 7px 8px 7px 10px;
    border: 1px solid rgba(142, 241, 226, 0.28);
    border-radius: 12px;
    background: rgba(10, 24, 29, 0.97);
    box-shadow: 0 18px 44px rgba(0, 4, 7, 0.44);
    display: grid;
    grid-template-columns: 28px 1fr auto 28px;
    align-items: center;
    gap: 8px;
  }
  .update-banner > span { width: 28px; height: 28px; display: grid; place-items: center; border-radius: 8px; color: #061c1c; background: var(--accent); }
  .update-banner p { margin: 0; display: grid; gap: 2px; }
  .update-banner strong { font-size: 11.5px; }
  .update-banner small { color: var(--text-muted); font-size: 9.5px; }
  .update-banner button { cursor: pointer; }
  .update-banner .review { height: 30px; padding: 0 11px; border-radius: 7px; color: #07191b; background: var(--accent); font-size: 10px; font-weight: 720; }
  .update-banner .dismiss { width: 28px; height: 28px; display: grid; place-items: center; color: var(--text-muted); }

  .update-backdrop { position: fixed; inset: 0; z-index: 80; padding: 18px; display: grid; place-items: center; background: rgba(2, 8, 12, 0.7); backdrop-filter: blur(6px); }
  .update-dialog { width: min(520px, 100%); max-height: min(680px, calc(100dvh - 36px)); overflow: auto; border: 1px solid var(--line-strong); border-radius: 15px; outline: none; color: var(--text); background: #0d1820; box-shadow: 0 28px 80px rgba(0, 3, 7, 0.58); }
  .update-dialog > header { min-height: 67px; padding: 14px 16px; border-bottom: 1px solid var(--line); display: flex; align-items: center; justify-content: space-between; }
  .update-dialog header span, .release-notes > span { color: var(--accent); font-size: 8px; font-weight: 760; letter-spacing: .12em; }
  .update-dialog h2 { margin: 3px 0 0; font-size: 18px; letter-spacing: -.02em; }
  .update-dialog header button { width: 32px; height: 32px; border-radius: 8px; display: grid; place-items: center; color: var(--text-muted); cursor: pointer; }
  .update-hero { min-height: 190px; padding: 30px 34px; text-align: center; display: grid; place-items: center; align-content: center; gap: 9px; background: radial-gradient(circle at 50% 5%, rgba(142, 241, 226, .13), transparent 58%); }
  .update-hero :global(svg) { color: var(--accent); }
  .update-hero[data-state="error"] :global(svg), .update-hero[data-state="disabled"] :global(svg) { color: var(--warning); }
  .update-hero h3 { margin: 2px 0 0; font-size: 20px; letter-spacing: -.025em; }
  .update-hero p { max-width: 390px; margin: 0; color: var(--text-muted); font-size: 11.5px; line-height: 1.55; overflow-wrap: anywhere; }
  .progress-number { color: var(--accent); font-size: 30px; }
  .progress-track { width: min(340px, 100%); height: 5px; overflow: hidden; border-radius: 999px; background: rgba(190, 220, 216, .1); }
  .progress-track i { display: block; height: 100%; border-radius: inherit; background: var(--accent); transition: width 160ms ease; }
  .release-notes { margin: 0 16px 14px; padding: 14px 16px; border: 1px solid var(--line); border-radius: 10px; background: rgba(4, 13, 18, .38); }
  .release-notes ul { margin: 9px 0 0; padding-left: 17px; color: #cbd8df; font-size: 10.5px; line-height: 1.55; }
  .release-notes li + li { margin-top: 4px; }
  .active-warning { margin: 0 16px 14px; padding: 10px 11px; border: 1px solid rgba(255, 207, 132, .2); border-radius: 8px; color: var(--warning); background: rgba(255, 207, 132, .07); display: flex; align-items: flex-start; gap: 8px; font-size: 10.5px; line-height: 1.4; }
  .update-dialog > footer { min-height: 64px; padding: 12px 16px; border-top: 1px solid var(--line); display: grid; grid-template-columns: auto 1fr auto auto; align-items: center; gap: 8px; }
  .update-dialog footer button { min-height: 34px; padding: 0 12px; border: 1px solid var(--line-strong); border-radius: 8px; color: #c8d6dc; display: inline-flex; align-items: center; justify-content: center; gap: 6px; cursor: pointer; font-size: 10.5px; font-weight: 650; }
  .update-dialog footer button.primary { border-color: transparent; color: #07191b; background: var(--accent); }
  .update-dialog footer button.link { padding-inline: 4px; border-color: transparent; color: var(--text-muted); }
  .update-dialog footer button:disabled { cursor: not-allowed; opacity: .46; }
  @media (max-width: 560px) { .update-dialog > footer { grid-template-columns: 1fr 1fr; } .update-dialog > footer div { display: none; } .update-dialog footer button { width: 100%; } }
</style>
