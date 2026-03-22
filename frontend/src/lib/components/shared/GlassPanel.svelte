<script lang="ts">
  import type { Snippet } from 'svelte';

  let {
    children,
    padding = 'var(--space-6)',
    radius = 'var(--radius-lg)',
    interactive = false,
    heavy = false,
    class: className = '',
  }: {
    children: Snippet;
    padding?: string;
    radius?: string;
    interactive?: boolean;
    heavy?: boolean;
    class?: string;
  } = $props();
</script>

<div
  class="{heavy ? 'glass-heavy' : 'glass'} {interactive ? 'glass-interactive' : ''} panel {className}"
  style="padding: {padding}; border-radius: {radius};"
>
  {@render children()}
</div>

<style>
  .panel {
    position: relative;
    overflow: hidden;
  }
</style>
