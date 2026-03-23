<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import Placeholder from '../components/shared/Placeholder.svelte';
  import type { Profile } from '../types/settings';
  import { getPluginDisplayName } from '../utils/plugin';

  let { profile }: { profile: Profile } = $props();

  let bindings = $derived(profile.bindings);
</script>

<div class="bindings">
  {#if bindings}
    <div class="bindings-grid">
      <GlassPanel>
        <h4 class="section-title">Pen Tip</h4>
        <div class="binding-row">
          <span class="binding-label">Action</span>
          <span class="binding-value">{getPluginDisplayName(bindings.tipButton)}</span>
        </div>
        <div class="binding-row">
          <span class="binding-label">Threshold</span>
          <span class="binding-value">{(bindings.tipActivationThreshold * 100).toFixed(0)}%</span>
        </div>
      </GlassPanel>

      <GlassPanel>
        <h4 class="section-title">Eraser</h4>
        <div class="binding-row">
          <span class="binding-label">Action</span>
          <span class="binding-value">{getPluginDisplayName(bindings.eraserButton)}</span>
        </div>
        <div class="binding-row">
          <span class="binding-label">Threshold</span>
          <span class="binding-value">{(bindings.eraserActivationThreshold * 100).toFixed(0)}%</span>
        </div>
      </GlassPanel>

      {#if bindings.penButtons?.length}
        <GlassPanel>
          <h4 class="section-title">Pen Buttons</h4>
          {#each bindings.penButtons as btn, i}
            <div class="binding-row">
              <span class="binding-label">Button {i + 1}</span>
              <span class="binding-value">{getPluginDisplayName(btn)}</span>
            </div>
          {/each}
        </GlassPanel>
      {/if}

      {#if bindings.auxButtons?.length}
        <GlassPanel>
          <h4 class="section-title">Auxiliary Buttons</h4>
          {#each bindings.auxButtons as btn, i}
            <div class="binding-row">
              <span class="binding-label">Button {i + 1}</span>
              <span class="binding-value">{getPluginDisplayName(btn)}</span>
            </div>
          {/each}
        </GlassPanel>
      {/if}
    </div>
  {:else}
    <Placeholder title="No Bindings" description="No binding data available for this tablet." />
  {/if}
</div>

<style>
  .bindings { max-width: 900px; }

  .bindings-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-4);
  }

  .section-title {
    font-size: var(--font-size-xs);
    font-weight: var(--font-weight-semibold);
    color: var(--text-muted);
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin: 0 0 var(--space-4) 0;
  }

  .binding-row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: var(--space-2) 0;
    border-bottom: 1px solid var(--divider);
  }

  .binding-row:last-child { border-bottom: none; }

  .binding-label { font-size: var(--font-size-sm); color: var(--text-secondary); }
  .binding-value { font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); color: var(--text-primary); font-family: var(--font-mono); }
</style>
