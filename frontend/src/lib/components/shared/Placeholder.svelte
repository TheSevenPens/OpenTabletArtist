<script lang="ts">
  import type { Snippet } from 'svelte';
  import GlassPanel from './GlassPanel.svelte';

  let {
    title,
    description = '',
    icon,
  }: {
    title: string;
    description?: string;
    icon?: Snippet;
  } = $props();
</script>

<GlassPanel>
  <div class="placeholder">
    {#if icon}
      <div class="placeholder-icon">{@render icon()}</div>
    {/if}
    <h3>{title}</h3>
    {#if description}<p>{description}</p>{/if}
  </div>
</GlassPanel>

<style>
  .placeholder {
    display: flex;
    flex-direction: column;
    align-items: center;
    text-align: center;
    gap: var(--space-4);
    padding: var(--space-10) 0;
  }

  .placeholder-icon {
    opacity: 0.5;
  }

  .placeholder h3 {
    font-size: var(--font-size-lg);
    font-weight: var(--font-weight-semibold);
    color: var(--text-secondary);
    margin: 0;
  }

  .placeholder p {
    font-size: var(--font-size-sm);
    color: var(--text-muted);
    max-width: 320px;
    margin: 0;
  }
</style>
