<script lang="ts">
  import type { Snippet } from 'svelte';

  let {
    children,
    variant = 'default',
    size = 'md',
    onclick,
    disabled = false,
    class: className = '',
  }: {
    children: Snippet;
    variant?: 'default' | 'accent' | 'ghost';
    size?: 'sm' | 'md';
    onclick?: () => void;
    disabled?: boolean;
    class?: string;
  } = $props();
</script>

<button
  class="btn btn-{variant} btn-{size} glass glass-interactive {className}"
  {onclick}
  {disabled}
>
  {@render children()}
</button>

<style>
  .btn {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
    font-weight: var(--font-weight-medium);
    transition: all var(--transition-smooth);
  }

  .btn-md {
    padding: var(--space-2) var(--space-4);
    font-size: var(--font-size-sm);
    border-radius: var(--radius-sm);
  }

  .btn-sm {
    padding: var(--space-1) var(--space-3);
    font-size: var(--font-size-xs);
    border-radius: var(--radius-xs);
  }

  .btn-accent {
    background: var(--accent-muted);
    border-color: var(--accent);
    color: var(--accent);
  }

  .btn-accent:hover {
    background: var(--accent);
    color: var(--text-inverse);
  }

  .btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
    pointer-events: none;
  }
</style>
