<script lang="ts">
  import type { Snippet } from 'svelte';

  let {
    title,
    subtitle = '',
    actions,
  }: {
    title: string;
    subtitle?: string;
    actions?: Snippet;
  } = $props();
</script>

<header class="page-header">
  {#if actions}
    <div class="page-header-row">
      <div>
        <h1 class="page-title">{title}</h1>
        {#if subtitle}<p class="page-subtitle">{subtitle}</p>{/if}
      </div>
      {@render actions()}
    </div>
  {:else}
    <h1 class="page-title">{title}</h1>
    {#if subtitle}<p class="page-subtitle">{subtitle}</p>{/if}
  {/if}
</header>

<style>
  .page-header {
    margin-bottom: var(--space-7);
  }

  .page-header-row {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: var(--space-4);
  }

  .page-title {
    font-size: var(--font-size-2xl);
    font-weight: var(--font-weight-bold);
    color: var(--text-primary);
    margin: 0 0 var(--space-1) 0;
  }

  .page-subtitle {
    font-size: var(--font-size-base);
    color: var(--text-secondary);
    margin: 0;
  }
</style>
